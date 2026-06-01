using System.Linq.Expressions;
using Geotrack.Domain.Entities.Core;

namespace Geotrack.Infrastructure.Services;

public static class PublicVisibilityQueryExtensions
{
    public const string PublishedStatus = "Published";

    public static Expression<Func<Destination, bool>> DestinationIsPublic() =>
        destination => destination.Status == PublishedStatus;

    public static Expression<Func<Location, bool>> LocationIsPublic() =>
        location =>
            location.Status == PublishedStatus &&
            location.Destination.Status == PublishedStatus;

    public static Expression<Func<Place, bool>> PlaceIsPublic() =>
        place =>
            place.Status == PublishedStatus &&
            place.Location.Status == PublishedStatus &&
            place.Location.Destination.Status == PublishedStatus;

    public static Expression<Func<Event, bool>> EventIsPublic() =>
        @event =>
            @event.Status == PublishedStatus &&
            @event.Destination.Status == PublishedStatus &&
            (@event.Location == null ||
                (@event.Location.Status == PublishedStatus &&
                 @event.Location.Destination.Status == PublishedStatus)) &&
            (@event.Place == null ||
                (@event.Place.Status == PublishedStatus &&
                 @event.Place.Location.Status == PublishedStatus &&
                 @event.Place.Location.Destination.Status == PublishedStatus));

    public static IQueryable<Destination> WherePublicDestinations(this IQueryable<Destination> query) =>
        query.Where(DestinationIsPublic());

    public static IQueryable<Location> WherePublicLocations(this IQueryable<Location> query) =>
        query.Where(LocationIsPublic());

    public static IQueryable<Place> WherePublicPlaces(this IQueryable<Place> query) =>
        query.Where(PlaceIsPublic());

    public static IQueryable<Event> WherePublicEvents(this IQueryable<Event> query) =>
        query.Where(EventIsPublic());
}
