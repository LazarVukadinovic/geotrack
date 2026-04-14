using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Location;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;
using Geotrack.Domain.Entities.Reviews;
using Geotrack.Domain.Entities.Core;

namespace Geotrack.Application.Services
{
    public class LocationService : ILocationService
    {
        private readonly ILocationRepository _locationRepository;
        private readonly IPlaceRepository _placeRepository;
        private readonly IEventRepository _eventRepository;

        public LocationService(ILocationRepository locationRepository,IPlaceRepository placeRepository,IEventRepository eventRepository)
        {
            _locationRepository = locationRepository;
            _placeRepository = placeRepository;
            _eventRepository = eventRepository;
        }

        public async Task<GetLocationDetailsResponseDto> GetLocationDetailsAsync(int locationId, int? userId)
        {
            var location = await _locationRepository.GetWithDetailsAsync(locationId);

            if (location == null) throw new KeyNotFoundException($"Lokacija sa ID-em {locationId} nije pronadjena.");

            var reviews = location.LocationReviews ?? new List<LocationReview>();
            var averageRating = reviews.Any() ? reviews.Average(r => r.Rating) : 0;
            var reviewsCount = reviews.Count;

            var sections = BuildSections(location);

            return new GetLocationDetailsResponseDto
            {
                Id = location.Id,
                Name = location.Name,
                DestinationId = location.DestinationId,
                LocationTypeId = location.LocationTypeId,
                Latitude = location.Latitude,
                Longitude = location.Longitude,
                CreatedAt = location.CreatedAt,
                UpdatedAt = location.UpdatedAt,
                AverageRating = Math.Round(averageRating, 1),
                ReviewsCount = reviewsCount,
                IsSaved = false,
                SavedCollectionIds = new List<int>(),
                Sections = sections
            };
        }

        private List<LocationSectionPreviewDto> BuildSections(Location location)
        {
            var sections = new List<LocationSectionPreviewDto>();

            var places = location.Places ?? new List<Place>();
            var events = location.Events ?? new List<Event>();

            if(places.Any())
            {
                sections.Add(new LocationSectionPreviewDto
                {
                    Key = "places",
                    Title = "Places",
                    Places = places.Take(5).Select(p =>new LocationSectionPlaceItemDto
                    { 
                        Id=p.Id,
                        Name=p.Name,
                        LocationId = p.LocationId
                    }).ToList(),Events = new()
                });
            }

            if (events.Any()) 
            {
                sections.Add(new LocationSectionPreviewDto
                {
                    Key = "events",
                    Title = "Events",
                    Places = new(),
                    Events = events.Take(5).Select(e => new LocationSectionEventItemDto
                    {
                        Id = e.Id,
                        LocationId = e.LocationId,
                        PlaceId = e.PlaceId,
                        StartDate = e.StartDate,
                        EndDate = e.EndDate
                    }).ToList()
                });
            }

            return sections;
        }

        public async Task<GetLocationSectionResponseDto> GetLocationSectionAsync(int locationId, string sectionKey)
        {
            var location = await _locationRepository.GetByIDAsync(locationId);
            if(location == null) throw new KeyNotFoundException($"Lokacija sa id-em {locationId} nije pronadjen.");

            var sectionTitle = sectionKey switch
            {
                "places" => "Places",
                "events" => "Events",
                _ => throw new KeyNotFoundException($"Nepoznati SectionKey: {sectionKey}")
            };

            var places = new List<LocationSectionPlaceItemDto>();
            var events = new List<LocationSectionEventItemDto>();


            if (sectionKey == "places" || sectionKey == "events")
            {
                var allPlaces = await _placeRepository.GetByLocationIdAsync(locationId);
                places = allPlaces.Select(p => new LocationSectionPlaceItemDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    LocationId = p.LocationId
                }).ToList();
            }

            return new GetLocationSectionResponseDto
            {
                
                Id = locationId,
                SectionKey = sectionKey,
                SectionTitle = sectionTitle,
                Places = places,
                Events = events
            };
        }



    }
}

