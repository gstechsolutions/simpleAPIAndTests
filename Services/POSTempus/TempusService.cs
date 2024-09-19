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
            var locations = new List<LocationModel>();
            var cacheKey = $"LocationList";

            try
            {
                if (!cache.TryGetValue(cacheKey, out locations))
                {
                    var dbList = await this.context.Locations
                        .Where(loc => loc.Active != null && (bool)loc.Active)
                        .ToListAsync();

                    locations = this.mapper.Map<List<Location>, List<LocationModel>>(dbList)
                        .OrderBy(loc => loc.LocationName)
                        .ToList();

                    //Put the list in cache for an hour
                    cache.Set(cacheKey, locations, TimeSpan.FromHours(12));
                }

            }
            catch (Exception ex)
            {
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

            return locations;

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
            }

            return tempusResponse;
        }

        public async Task<CorcentricTempusPaymentResponse> PaymentCorcentricTempusMethods_Select(CorcentricTempusPaymentRequest tempusReq)
        {
            var tempusResponse = new CorcentricTempusPaymentResponse();
            var functionName = "PaymentCorcentricTempusMethods_Select";

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
                        var serializer = new XmlSerializer(typeof(CorcentricTempusPaymentResponse));
                        using (var reader = new StringReader(responseString))
                        {
                            tempusResponse = (CorcentricTempusPaymentResponse)serializer.Deserialize(reader);

                            //if error then don't Generate the signature
                            if(tempusResponse != null && tempusResponse.TRANRESP != null && !string.IsNullOrEmpty(tempusResponse.TRANRESP.SIGDATA))
                            {
                                tempusResponse.FILENAME = await GenerateSignature(tempusResponse.TRANRESP.SIGDATA, tempusResponse.SESSIONID);
                            }                            
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
            }

            return tempusResponse;
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
                    int width = points.Max(p => p.X) + 10;  // Adding some margin
                    int height = points.Max(p => p.Y) + 10; // Adding some margin
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
        
    }

}
