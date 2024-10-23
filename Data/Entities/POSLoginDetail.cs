using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace tempus.service.core.api.Data.Entities
{
    [Table("tblPOSLoginDetails")]
    public class POSLoginDetail
    {
        [Key]
        public long POSLoginID { get; set; }

        public long EmpID { get; set; }

        public string HostIP { get; set; } = string.Empty;

        public string HostName { get; set; } = string.Empty;

        public DateTime LoginDateTime { get; set; }

        public DateTime? LogoutDateTime { get; set; }

        public bool LoginStatus { get; set; }

        public long? CompanyDepartmentID { get; set; }

        public string? DeviceAlias { get; set; }

        public int? POSDeviceConfigurationID { get; set; }


    }
}
