using tempus.service.core.api.Models.POSTempus;
using System.Drawing;


namespace tempus.service.core.api.Services.POSTempus
{
    public interface ITempusService
    {
        Task<PaymentTempusMethodResponse> PaymentTempusMethods_Select(PaymentTempusMethodRequest tempusReq);

        Task<CorcentricTempusPaymentResponse> PaymentCorcentricTempusMethods_Select(CorcentricTempusPaymentRequest tempusReq);

        Task<string> GenerateSignature(string sigdata, string fileName);

        Task<List<LocationModel>> GetLocations();
    }
}
