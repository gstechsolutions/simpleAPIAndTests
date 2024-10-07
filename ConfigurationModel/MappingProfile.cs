using AutoMapper;
using tempus.service.core.api.Data.Entities;
using tempus.service.core.api.Models;
using tempus.service.core.api.Models.POSTempus;

namespace tempus.service.core.api.ConfigurationModel
{
    public class MappingProfile : Profile
    {
        //this.CreateMap<Comment, CommentModel>()
        //        .ForMember(dest => dest.PostedBy, o => o.MapFrom(source => $"{source.CreatedBy.FirstName} {source.CreatedBy.LastName}"))
        //        .ForMember(dest => dest.PostedOn, o => o.MapFrom(source => source.CreatedOn))
        //        .ForMember(dest => dest.Comment, o => o.MapFrom(source => source.Text))
        //        ;

        public MappingProfile()
        {
            this.CreateMap<Location, LocationModel>()
                .ReverseMap();

            this.CreateMap<PosInvoice, PosInvoiceModel>()
                ;

            this.CreateMap<SISPosInvoice, SISPosInvoiceModel>()
                ;

            this.CreateMap<SISPosInvoice, PosInvoiceModel>()
                ;
        }
    }
}
