using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using tempus.service.core.api.Models;
using tempus.service.core.api.Models.POSTempus;
using tempus.service.core.api.Services.POSTempus;

namespace tempus.service.core.api.Controllers
{
    [ApiController]
    public class TempusController : ControllerBase
    {
        private readonly ITempusService service;

        public TempusController(ITempusService service)
        {
            this.service = service;
        }

        [HttpPost]
        [Route("api/tempus/saleauth")]
        [SwaggerOperation(OperationId = "PaymentTempusMethods_Select")]
        [SwaggerResponse(statusCode: 200, type: typeof(PaymentTempusMethodRequest), description: "Used to call Tempus for auth sale")]
        public async Task<IActionResult> PaymentTempusMethods_Select([FromBody] PaymentTempusMethodRequest order)
        {
            var response = await service.PaymentTempusMethods_Select(order);
            return Ok(response);
        }

        //public async Task<CorcentricTempusPaymentResponse> PaymentCorcentricTempusMethods_Select(CorcentricTempusPaymentRequest tempusReq)

        [HttpPost]
        [Route("api/tempus/pay/corcentric")]
        [SwaggerOperation(OperationId = "PaymentCorcentricTempusMethods_Select")]
        [SwaggerResponse(statusCode: 200, type: typeof(CorcentricTempusPaymentResponse), description: "Used to call Tempus for corcentric sale")]
        public async Task<IActionResult> PaymentCorcentricTempusMethods_Select([FromBody] CorcentricTempusPaymentRequest order)
        {
            var response = await service.PaymentCorcentricTempusMethods_Select(order);
            return Ok(response);
        }

        [HttpGet]
        [Route("api/tempus/location/list")]
        [SwaggerOperation(OperationId = "GetLocations")]
        [SwaggerResponse(statusCode: 200, type: typeof(List<LocationModel>), description: "Used to retrieve list of locations.")]
        public async Task<IActionResult> GetLocations()
        {
            var list = await service.GetLocations();
            return Ok(list);
        }

        [HttpPost]
        [Route("api/tempus/invoice")]
        [SwaggerOperation(OperationId = "GetPosInvoices")]
        [SwaggerResponse(statusCode: 200, type: typeof(List<PosInvoiceModel>), description: "Used to call Tempus for corcentric sale")]
        public async Task<IActionResult> GetPosInvoices([FromBody] PosFiltersModel invoice)
        {
            var response = await service.GetPosInvoices(invoice);
            return Ok(response);
        }

        [HttpPost]
        [Route("api/tempus/pay/credit")]
        [SwaggerOperation(OperationId = "PaymentCreditTempusMethods_Select")]
        [SwaggerResponse(statusCode: 200, type: typeof(PaymentTempusMethodResponse), description: "Used to call Tempus for credit sale")]
        public async Task<IActionResult> PaymentCreditTempusMethods_Select([FromBody] PaymentTempusMethodRequest order)
        {
            var response = await service.PaymentCreditTempusMethods_Select(order);
            return Ok(response);
        }
    }
}
