using Microsoft.EntityFrameworkCore;
using OperaHouse.Booking.Application.Bookings;
using OperaHouse.Booking.Infrastructure.Persistence;
using BookingEntity = OperaHouse.Booking.Domain.Bookings.Booking;

namespace OperaHouse.Booking.Infrastructure.Bookings;

public sealed class BookingRepository(BookingDbContext dbContext) : IBookingRepository
{
    public async Task<BookingEntity?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        return await dbContext.Bookings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                booking => booking.Id == id,
                cancellationToken);
    }

    public async Task AddAsync(
        BookingEntity booking,
        CancellationToken cancellationToken)
    {
        await dbContext.Bookings.AddAsync(
            booking,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
