using AutoMapper;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Application.Performances;
using OperaHouse.Booking.Domain.Performances;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Application.Mapping;

public sealed class BookingMappingProfile : Profile
{
    public BookingMappingProfile()
    {
        CreateMap<BookingEntity, BookingDto>();
        CreateMap<Performance, PerformanceDto>();
    }
}
