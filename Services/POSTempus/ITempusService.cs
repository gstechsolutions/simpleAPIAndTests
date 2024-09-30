using tempus.service.core.api.Models.POSTempus;
using System.Drawing;
using tempus.service.core.api.Models;


namespace tempus.service.core.api.Services.POSTempus
{
    public interface ITempusService
    {
        Task<PaymentTempusMethodResponse> PaymentTempusMethods_Select(PaymentTempusMethodRequest tempusReq);

        Task<CorcentricTempusPaymentResponse> PaymentCorcentricTempusMethods_Select(CorcentricTempusPaymentRequest tempusReq);

        Task<string> GenerateSignature(string sigdata, string fileName);

        Task<List<LocationModel>> GetLocations();

        Task<List<PosInvoiceModel>> GetPosInvoices(PosFiltersModel filters);

        //this is the one use for credut auth sales
        Task<PaymentTempusMethodResponse> PaymentCreditTempusMethods_Select(PaymentTempusMethodRequest tempusReq);
    }
}
