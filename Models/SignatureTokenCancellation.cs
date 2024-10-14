namespace tempus.service.core.api.Models
{
    public class SignatureTokenCancellation
    {
        public string SubscriberKey { get; set; }

        public CancellationTokenSource CancellationTokenSource { get; set; }
    }
}
