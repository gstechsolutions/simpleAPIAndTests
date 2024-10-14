using System.Xml.Serialization;

namespace tempus.service.core.api.Models.POSTempus
{
    [XmlRoot("TTMESSAGE")]
    public class InteractiveCancelTempusRequest
    {
        
        public int TTMESSAGETIMEOUT { get; set; }

        
        public AuthInfo AUTHINFO { get; set; }

        
        public InteractiveCancelTransaction TRANSACTION { get; set; }
    }

    //public class InteractiveCancelAuthInfo
    //{
    //    [XmlElement("SUBSCRIBERKEY")]
    //    public string SUBSCRIBERKEY { get; set; }

    //    [XmlElement("RNID")]
    //    public string RNID { get; set; }

    //    [XmlElement("RNCERT")]
    //    public string RNCERT { get; set; }
    //}

    public class InteractiveCancelTransaction
    {
        
        public string TRANSACTIONTYPE { get; set; }


    }

}
