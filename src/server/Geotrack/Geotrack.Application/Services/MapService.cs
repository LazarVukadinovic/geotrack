using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.Map;
using Geotrack.Application.Interfaces.Repository;
using Geotrack.Application.Interfaces.Service;

namespace Geotrack.Application.Services
{
    public class MapService : IMapService
    {
        private readonly IDestinationRepository _destinationRepository;
        private readonly ILocationRepository _locationRepository;
        private readonly IPlaceRepository _placeRepository;
        private readonly IEventRepository _eventRepository;

        private static readonly HashSet<string> AllTypes = new()
        { "destination", "location", "place", "event" };

        public MapService(IDestinationRepository destinationRepository,ILocationRepository locationRepository,IPlaceRepository placeRepository,IEventRepository eventRepository)
        {
            _destinationRepository = destinationRepository;
            _locationRepository = locationRepository;
            _placeRepository = placeRepository;
            _eventRepository = eventRepository;
        }
        
        public async Task<List<MapSearchResultDto>> SearchAsync(string query, IEnumerable<string>? types)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<MapSearchResultDto>();

            var typesToSearch = types
                ?.Select(t => t.ToLower().Trim())
                .Where(t => AllTypes.Contains(t))
                .ToHashSet();

            if (typesToSearch == null || typesToSearch.Count == 0)
                typesToSearch = AllTypes;

            var results = new List<MapSearchResultDto>();
            var tasks = new List<Task>();

            if (typesToSearch.Contains("destination"))
                tasks.Add(Task.Run(async () =>
                {
                    var items = await _destinationRepository.SearchByNameAsync(query);
                    lock (results)
                        results.AddRange(items.Select(d => new MapSearchResultDto
                        {
                            Id = d.Id,
                            Type = "destination",
                            Name = d.Name,
                            Description = null,
                            Latitude = d.Latitude,
                            Longitude = d.Longitude,
                            ParentName = null
                        }));
                }));

            if (typesToSearch.Contains("location"))
                tasks.Add(Task.Run(async () =>
                {
                    var items = await _locationRepository.SearchByNameAsync(query);
                    lock (results)
                        results.AddRange(items.Select(l => new MapSearchResultDto
                        {
                            Id = l.Id,
                            Type = "location",
                            Name = l.Name,
                            Description = null,
                            Latitude = l.Latitude,
                            Longitude = l.Longitude,
                            ParentName = l.Destination?.Name
                        }));
                }));

            if (typesToSearch.Contains("place"))
                tasks.Add(Task.Run(async () =>
                {
                    var items = await _placeRepository.SearchByNameAsync(query);
                    lock (results)
                        results.AddRange(items.Select(p => new MapSearchResultDto
                        {
                            Id = p.Id,
                            Type = "place",
                            Name = p.Name,
                            Description = null,
                            Latitude = p.Latitude,
                            Longitude = p.Longitude,
                            ParentName = p.Location?.Name
                        }));
                }));

            if (typesToSearch.Contains("event"))
                tasks.Add(Task.Run(async () =>
                {
                    var items = await _eventRepository.SearchByNameAsync(query);
                    lock (results)
                        results.AddRange(items.Select(e => new MapSearchResultDto
                        {
                            Id = e.Id,
                            Type = "event",
                            Name = e.Name,
                            Description = null,
                            Latitude = e.Place?.Latitude ?? 0,
                            Longitude = e.Place?.Longitude ?? 0,
                            ParentName = e.Place?.Name
                        }));
                }));

            await Task.WhenAll(tasks);

            return results;
        }
    }
}
