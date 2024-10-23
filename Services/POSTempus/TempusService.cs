using AutoMapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NodaTime;
using System.Text;
using System.Xml.Linq;
using System.Xml.Serialization;
using System.Xml;
using tempus.service.core.api.ConfigurationModel;
using tempus.service.core.api.Models.POSTempus;
using tempus.service.core.api.Data;
using Microsoft.EntityFrameworkCore;
using tempus.service.core.api.Data.Entities;
using System.Drawing;
using tempus.service.core.api.Models;
using Microsoft.Data.SqlClient;

namespace tempus.service.core.api.Services.POSTempus
{
    public class TempusService : ITempusService
    {
        private static readonly HttpClient client = new HttpClient();        
        private readonly STRDMSContext context;
        private readonly ILogger<TempusService> logger;
        private readonly IMapper mapper;
        private readonly IOptions<ServiceCoreSettings> settings;
        private readonly IClock clock;
        private readonly IMemoryCache cache;        

        public TempusService(STRDMSContext context,
            ILogger<TempusService> logger,
            IMapper mapper,
            IOptions<ServiceCoreSettings> settings,
            IClock clock,
            IMemoryCache cache)
        {
            this.context = context;
            this.logger = logger;
            this.mapper = mapper;
            this.settings = settings;
            this.clock = clock;
            this.cache = cache;
        }

        public async Task<List<LocationModel>> GetLocations()
        {
            var functionName = "GetLocations";
            var locationsList = new List<LocationModel>();
            var cacheKey = $"LocationList";

            try
            {
                if (!cache.TryGetValue(cacheKey, out locationsList))
                {
                    var dbList = await this.context.Locations
                        .Where(loc => loc.Active != null && (bool)loc.Active)
                        .ToListAsync();

                    locationsList = this.mapper.Map<List<Location>, List<LocationModel>>(dbList)
                        .OrderBy(loc => loc.LocationName)
                        .ToList();

                    //Put the list in cache for an hour
                    cache.Set(cacheKey, locationsList, TimeSpan.FromHours(12));
                }

            }
            catch (Exception ex)
            {
                var error = new LocationModel();
                error.SetError(ex.Message);
                locationsList = new List<LocationModel>();
                locationsList.Add(error);
                this.logger.LogError($"{functionName} EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    this.logger.LogError($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.InnerException.Message}");
                }
            }
            finally
            {
                this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
            }

            return locationsList;

        }

        /// <summary>
        /// It is the response that needs to be set as an abstraction because it could be Corcentric response, or creditAuth ressponse, AR response 
        /// based on the request from POS payment in Phonenix.
        /// </summary>
        /// <param name="tempusReq"></param>
        /// <returns></returns>
        public async Task<PaymentTempusMethodResponse> PaymentTempusMethods_Select(PaymentTempusMethodRequest tempusReq)
        {
            var tempusResponse = new PaymentTempusMethodResponse();
            var functionName = "PaymentTempusMethods_Select";

            using (var client = new HttpClient())
            {
                try
                {
                    // Serialize the object to XML
                    string payload = SerializeToXml(tempusReq);

                    XDocument document = XDocument.Parse(payload);
                    var rootElements = document.Root.Elements();
                    XDocument newDoc = new XDocument(new XElement("TTMESSAGE", rootElements));
                    payload = newDoc.ToString();

                    // Create the request content
                    var content = new StringContent(payload, Encoding.UTF8, "application/xml");

                    // Send the POST request
                    var response = await client.PostAsync(this.settings.Value.TempusUri, content);

                    if (response.IsSuccessStatusCode)
                    {
                        // Read response as string
                        var responseString = await response.Content.ReadAsStringAsync();

                        // Deserialize the XML response into the C# class
                        var serializer = new XmlSerializer(typeof(PaymentTempusMethodResponse));
                        using (var reader = new StringReader(responseString))
                        {
                            tempusResponse = (PaymentTempusMethodResponse)serializer.Deserialize(reader);
                            tempusResponse.FILENAME = await GenerateSignature(tempusResponse.TRANRESP.SIGDATA, tempusResponse.SESSIONID);
                        }
                    }
                    else
                    {
                        this.logger.LogError($"{functionName} ERROR - {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError($"{functionName} EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
                }
                finally
                {
                    this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
                }
            }

            return tempusResponse;
        }


        public async Task<CorcentricTempusPaymentResponse> PaymentCorcentricTempusMethods_Select(CorcentricTempusPaymentRequest tempusReq)
        {
            var tempusResponse = new CorcentricTempusPaymentResponse();
            var functionName = "PaymentCorcentricTempusMethods_Select";
            var cacheKey = "SignatureCancellationTokenList";
            var tokenCancellationList = new List<SignatureTokenCancellation>();
            var tokenToCancel = new SignatureTokenCancellation();

            //var cancellationToken = tempusReq.CancellationToken?.Token ?? CancellationToken.None;

            using (var client = new HttpClient())
            {
                try
                {
                    tokenCancellationList = SetTokenCachedList(tempusReq, cacheKey);
                    if (tokenCancellationList != null && tokenCancellationList.Count > 0)
                    {
                        tokenToCancel = tokenCancellationList.Find(t => t.SubscriberKey == tempusReq.AUTHINFO.SUBSCRIBERKEY);
                    }

                    // Serialize the object to XML
                    string payload = SerializeToXml(tempusReq);

                    XDocument document = XDocument.Parse(payload);
                    var rootElements = document.Root.Elements();
                    XDocument newDoc = new XDocument(new XElement("TTMESSAGE", rootElements));
                    payload = newDoc.ToString();

                    // Create the request content
                    var content = new StringContent(payload, Encoding.UTF8, "application/xml");

                    // Send the POST request
                    var response = await client.PostAsync(this.settings.Value.TempusUri, content, tokenToCancel.CancellationTokenSource.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        // Read response as string
                        var responseString = await response.Content.ReadAsStringAsync();

                        // Deserialize the XML response into the C# class
                        var serializer = new XmlSerializer(typeof(CorcentricTempusPaymentResponse));
                        using (var reader = new StringReader(responseString))
                        {
                            tempusResponse = (CorcentricTempusPaymentResponse)serializer.Deserialize(reader);

                            //if error then don't Generate the signature
                            if (tempusResponse != null && tempusResponse.TRANRESP != null && !string.IsNullOrEmpty(tempusResponse.TRANRESP.SIGDATA))
                            {
                                tempusResponse.FILENAME = await GenerateSignature(tempusResponse.TRANRESP.SIGDATA, tempusResponse.SESSIONID);
                            }
                            else if ((bool)(tempusResponse.TTMSGTRANSUCCESS?.ToLower().Equals("false")))
                            {
                                tempusResponse.ResponseMessage = $"Error processing signature";
                            }
                        }
                    }
                    else
                    {
                        this.logger.LogError($"{functionName} ERROR - {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {response.StatusCode}");
                    }
                }
                catch (TaskCanceledException ex)
                {
                    // This exception is thrown if the task was canceled
                    tempusResponse.ResponseMessage = $"ERROR: {ex.Message}";
                    this.logger.LogError($"Http call canceled to get signature :: EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    this.logger.LogError($"{functionName} EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
                }
                finally
                {
                    this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
                }
            }

            return tempusResponse;
        }

        private List<SignatureTokenCancellation> SetTokenCachedList(CorcentricTempusPaymentRequest tempusReq, string cacheKey)
        {
            var tokenCancellationList = new List<SignatureTokenCancellation>();
            if (!cache.TryGetValue(cacheKey, out tokenCancellationList))
            {
                tokenCancellationList = new List<SignatureTokenCancellation>();
                var newToken = new SignatureTokenCancellation
                {
                    SubscriberKey = tempusReq.AUTHINFO.SUBSCRIBERKEY,
                    CancellationTokenSource = new CancellationTokenSource()
                };
                tokenCancellationList.Add(newToken);

                cache.Set(cacheKey, tokenCancellationList, TimeSpan.FromHours(1));
            }
            else
            {
                //the tokenList exist but check if existing SubscriberKey
                var ix = tokenCancellationList.FindIndex(t => t.SubscriberKey == tempusReq.AUTHINFO.SUBSCRIBERKEY);
                if (ix != -1)
                {
                    //replace the existing token for the new request
                    tokenCancellationList[ix] = new SignatureTokenCancellation
                    {
                        SubscriberKey = tempusReq.AUTHINFO.SUBSCRIBERKEY,
                        CancellationTokenSource = new CancellationTokenSource()
                    };
                }
                //is not on the list, so add it
                else
                {
                    tokenCancellationList.Add(new SignatureTokenCancellation
                    {
                        SubscriberKey = tempusReq.AUTHINFO.SUBSCRIBERKEY,
                        CancellationTokenSource = new CancellationTokenSource()
                    });
                }

                cache.Set(cacheKey, tokenCancellationList, TimeSpan.FromHours(1));
            }

            return tokenCancellationList;
        }

        public async Task<PosFiltersModel> CancelHttpClientRequest(PosFiltersModel filter)
        {
            var functionName = "CancelHttpClientRequest";
            List<SignatureTokenCancellation>? tokenCancellationList;
            var cacheKey = "SignatureCancellationTokenList";



            try
            {
                if (!cache.TryGetValue(cacheKey, out tokenCancellationList))
                {
                    this.logger.LogError($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Cancellation token does not exist for: {filter.SubscriberKey}");
                }
                else
                {
                    //tokenList does exist now look for the token to cancel http request by SubscriberKey
                    var tokenToCancel = tokenCancellationList.Find(t => t.SubscriberKey == filter.SubscriberKey);
                    if (tokenToCancel != null)
                    {
                        //now that you have the token, cancel it
                        tokenToCancel.CancellationTokenSource.Cancel();
                        tokenCancellationList.Remove(tokenToCancel);
                        this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Token cancelled SubscriberKey: {filter.SubscriberKey}.");
                    }
                }
                
            }
            catch (Exception ex)
            {
                this.logger.LogError($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.InnerException.Message}");
            }
            finally
            {
                this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
            }

            return await Task.FromResult(filter);
        }

        public async Task<string> GenerateSignature(string sigdata, string fileName)
        {
            var functionName = "GenerateSignature";
            var dirSigPath = $@"{this.settings.Value.SignatureFolder}";
            bool success = false;
            string filePath = string.Empty;

            try
            {
                if (!string.IsNullOrEmpty(sigdata))
                {
                    var points = sigdata
                   .Split('(')
                   .Where(p => !string.IsNullOrEmpty(p) && p.Contains(","))
                   .Select(p => p.TrimEnd(')').Split(','))
                   .Select(p => new
                   {
                       X = int.Parse(p[0]),
                       Y = int.Parse(p[1]),
                       Pressure = int.Parse(p[2]) // Not used in this example
                   })
                   .ToList();

                    // Create a bitmap to draw the signature
                    int width = points.Max(p => p.X) + 20;  // Adding some margin
                    int height = points.Max(p => p.Y) + 20; // Adding some margin
                    Bitmap bmp = new Bitmap(width, height);

                    // Draw on the bitmap
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White); // Background color

                        // Pen for drawing lines
                        Pen pen = new Pen(Color.Black, 2);

                        for (int i = 1; i < points.Count; i++)
                        {
                            // Check if pressure indicates pen lift (pressure = 1 means lift, 0 means draw)
                            if (points[i - 1].Pressure == 0 && points[i].Pressure == 0)
                            {
                                // Draw a line between two consecutive points
                                g.DrawLine(pen, points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y);
                            }
                        }
                    }

                    // Check if the directory exists
                    if (!Directory.Exists(dirSigPath))
                    {
                        // Create the directory if it does not exist
                        Directory.CreateDirectory(dirSigPath);
                    }
                    filePath = Path.Combine(dirSigPath, $"{fileName}.png");

                    // Save the image to the specified file path
                    using (bmp)
                    {
                        bmp.Save(filePath);
                        success = true; // Ensure no issues with file access here
                    }
                }
                // Parse the sigdata into a list of points
               
            }
            catch (Exception ex)
            {
                this.logger.LogError($"{functionName} EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
            }
            finally
            {
                this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
            }

            return await Task.FromResult(filePath);
        }

        private static string SerializeToXml<T>(T obj)
        {
            if (obj == null)
            {
                throw new ArgumentNullException(nameof(obj));
            }

            XmlSerializer serializer = new XmlSerializer(typeof(T));

            // Create XmlWriterSettings to control the XML output format
            XmlWriterSettings settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,  // Exclude XML declaration/header
                Indent = false,             // No indentation, remove new lines
                NewLineHandling = NewLineHandling.None
            };

            using (StringWriter stringWriter = new StringWriter())
            using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter, settings))
            {
                serializer.Serialize(xmlWriter, obj);
                return stringWriter.ToString();
            }
        }

        public async Task<List<PosInvoiceModel>> GetSIPPosInvoices(PosFiltersModel filters)
        {
            var functionName = "GetPosInvoices";
            var invoices = new List<PosInvoiceModel>();
            var sisInvoices = new List<SISPosInvoiceModel>();

            try
            {
                var list = await context.POSInvoices.FromSqlRaw("EXEC [dbo].[POSInvoice_Select] @InvoiceNumber",
                          new SqlParameter("@InvoiceNumber", filters.SalesNo)).ToListAsync();

                invoices = this.mapper.Map<List<PosInvoice>, List<PosInvoiceModel>>(list);

            }
            catch (Exception ex)
            {
                var error = new PosInvoiceModel();
                error.SetError(ex.Message);
                invoices.Add(error);
                this.logger.LogError($"{functionName} EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    this.logger.LogError($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.InnerException.Message}");
                }
            }
            finally
            {
                this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
            }

            return invoices;
        }

        public async Task<List<SISPosInvoiceModel>> GetSISPosInvoices(PosFiltersModel filters)
        {
            var functionName = "GetSISPosInvoices";
            var invoices = new List<SISPosInvoiceModel>();
            

            try
            {
                var list = await context.SISPOSInvoices.FromSqlRaw("EXEC [dbo].[POSInvoice_Select] @InvoiceNumber",
                          new SqlParameter("@InvoiceNumber", filters.SalesNo)).ToListAsync();

                invoices = this.mapper.Map<List<SISPosInvoice>, List<SISPosInvoiceModel>>(list);

            }
            catch (Exception ex)
            {
                var error = new SISPosInvoiceModel();
                error.SetError(ex.Message);
                invoices.Add(error);
                this.logger.LogError($"{functionName} EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    this.logger.LogError($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.InnerException.Message}");
                }
            }
            finally
            {
                this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
            }

            return invoices;
        }

        public async Task<PaymentTempusMethodResponse> PaymentCreditTempusMethods_Select(PaymentTempusMethodRequest tempusReq)
        {
            var tempusResponse = new PaymentTempusMethodResponse();
            var functionName = "PaymentCreditTempusMethods_Select";

            using (var client = new HttpClient())
            {
                try
                {
                    // Serialize the object to XML
                    string payload = SerializeToXml(tempusReq);

                    XDocument document = XDocument.Parse(payload);
                    var rootElements = document.Root.Elements();
                    XDocument newDoc = new XDocument(new XElement("TTMESSAGE", rootElements));
                    payload = newDoc.ToString();

                    // Create the request content
                    var content = new StringContent(payload, Encoding.UTF8, "application/xml");

                    // Send the POST request
                    var response = await client.PostAsync(this.settings.Value.TempusUri, content);

                    if (response.IsSuccessStatusCode)
                    {
                        // Read response as string
                        var responseString = await response.Content.ReadAsStringAsync();

                        // Deserialize the XML response into the C# class
                        var serializer = new XmlSerializer(typeof(PaymentTempusMethodResponse));
                        using (var reader = new StringReader(responseString))
                        {
                            tempusResponse = (PaymentTempusMethodResponse)serializer.Deserialize(reader);
                        }
                    }
                    else
                    {
                        this.logger.LogError($"{functionName} ERROR - {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError($"{functionName} EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
                }
                finally
                {
                    this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
                }
            }

            return tempusResponse;
        }

        public async Task<InteractiveCancelTempusResponse> InteractiveCancelTempusMethods_Select(InteractiveCancelTempusRequest tempusReq)
        {
            var tempusResponse = new InteractiveCancelTempusResponse();
            var functionName = "InteractiveCancelTempusMethods_Select";

            using (var client = new HttpClient())
            {
                try
                {
                    // Serialize the object to XML
                    string payload = SerializeToXml(tempusReq);

                    XDocument document = XDocument.Parse(payload);
                    var rootElements = document.Root.Elements();
                    XDocument newDoc = new XDocument(new XElement("TTMESSAGE", rootElements));
                    payload = newDoc.ToString();

                    // Create the request content
                    var content = new StringContent(payload, Encoding.UTF8, "application/xml");

                    // Send the POST request
                    var response = await client.PostAsync(this.settings.Value.TempusUri, content);

                    if (response.IsSuccessStatusCode)
                    {
                        // Read response as string
                        var responseString = await response.Content.ReadAsStringAsync();

                        // Deserialize the XML response into the C# class
                        var serializer = new XmlSerializer(typeof(InteractiveCancelTempusResponse));
                        using (var reader = new StringReader(responseString))
                        {
                            tempusResponse = (InteractiveCancelTempusResponse)serializer.Deserialize(reader);                           
                        }
                    }
                    else
                    {
                        this.logger.LogError($"{functionName} ERROR - {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    this.logger.LogError($"{functionName} EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
                }
                finally
                {
                    this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
                }
            }

            return tempusResponse;
        }

        public async Task<List<POSDeviceConfigurationModel>> GetPOSDeviceConfigurationByHostName(PosFiltersModel filters)
        {
            var functionName = "GetPOSDeviceConfigurationByHostName";
            var tempusConfigList = new List<TempusDeviceConfigurationModel>();      
            var tempusIncludeList = new List<POSDeviceConfigurationModel>();

            try
            {
                //execute the SP [POSDeviceConfigurationHostName_Select] with arguments hostName and EmpID,
                //that will return {POSDeviceConfigurationID	DeviceAlias	IsDefault}
                //and based on the default DeviceAlias for this user belonging to a company and department
                //select all the config setting using the alias.  use the two entities created POSConfiguration
                //and POSDeviceConfiguration
                var list = await context.POSDeviceConfigurationHostNames.FromSqlRaw("EXEC [dbo].[POSDeviceConfigurationHostName_Select] @HostName, @EmployeeID",
                          new SqlParameter("@HostName", filters.HostName),
                          new SqlParameter("@EmployeeID", filters.EmployeeID)).ToListAsync();

                var hostList = this.mapper.Map<List<POSDeviceConfigurationHostName>, List<POSDeviceConfigurationHostNameModel>>(list);

                //from the aliasList get the default one and 
                //tempusConfigList = await context.POSConfigurations.Join(context.POSDeviceConfigurations,
                //    c => new { c.CompanyID, c.CompanyDepartmentID },
                //    dc => new { dc.CompanyID, dc.CompanyDepartmentID },
                //    (c, dc) => new TempusDeviceConfigurationModel
                //    {
                //        RNID = c.RNID,
                //        RNCert = c.RNCert,
                //        Subscriberkey = dc.Subscriberkey
                //    }).ToListAsync();

                if (hostList != null)
                {
                    var emp = await context.Employees.Where(e => e.EmployeeID == filters.EmployeeID).FirstOrDefaultAsync();

                    if (emp != null)
                    {
                        var resultWithNavProp = await context.POSDeviceConfigurations
                            .Where(dc => dc.CompanyID == emp.HomeCompanyID && dc.CompanyDepartmentID == emp.HomeCompanyDepartmentID && dc.Active == 1)
                            .Include(dc => dc.POSConfiguration)
                            .ToListAsync();

                        if (resultWithNavProp != null)
                        {
                            tempusIncludeList = mapper.Map<List<POSDeviceConfiguration>, List<POSDeviceConfigurationModel>>(resultWithNavProp);
                            //set the default one
                            var includeDefault = tempusIncludeList.Where(dc => dc.HostName.ToLower().Equals(filters.HostName?.ToLower())).FirstOrDefault();
                            if (includeDefault != null)
                            {
                                includeDefault.IsDefault = 1;
                            }
                        }
                    }
                    var success = true;
                }                
            }
            catch (Exception ex)
            {
                var error = new TempusDeviceConfigurationModel();
                error.SetError(ex.Message);
                tempusConfigList.Add(error);
                this.logger.LogError($"{functionName} EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    this.logger.LogError($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.InnerException.Message}");
                }
            }
            finally
            {
                this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
            }

            return tempusIncludeList;
        }

        public async Task<POSDeviceConfigurationModel> SetPOSDeviceInLoginDetails(POSDeviceConfigurationModel model)
        {
            var functionName = "SetPOSDeviceInLoginDetails";            

            try
            {
                var detailsToUpdate = await context.POSLoginDetails
                    .Where(det => det.EmpID == model.EmpID)
                    .OrderByDescending(det => det.LoginDateTime)
                    .Take(1)
                    .FirstOrDefaultAsync();

                //only update POSLoginDetails if loginStatus == 1 and LogoutDateTime is null
                if (detailsToUpdate != null && detailsToUpdate.LoginStatus && !detailsToUpdate.LogoutDateTime.HasValue)
                {
                    detailsToUpdate.DeviceAlias = model.DeviceAlias;
                    detailsToUpdate.POSDeviceConfigurationID = model.POSDeviceConfigurationID;
                    // Save the changes to the database
                    await context.SaveChangesAsync();

                    this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Successfully updated POSLoginDetails for EmpID {model.EmpID}.");
                }

            }
            catch (Exception ex)
            {
                model.SetError(ex.Message);

                this.logger.LogError($"{functionName} EXCEPTION- {clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    this.logger.LogError($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: {ex.InnerException.Message}");
                }
            }
            finally
            {
                this.logger.LogInformation($"{clock.GetCurrentInstant().ToDateTimeUtc().ToLocalTime()}: Exited {functionName}.");
            }

            return model;
        }
    }

}
