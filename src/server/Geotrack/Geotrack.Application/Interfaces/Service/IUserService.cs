using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Geotrack.Application.DTOs.User;
using Geotrack.Application.DTOs.Collection;
using Geotrack.Application.DTOs.Interest;

namespace Geotrack.Application.Interfaces.Service;

public interface IUserService
{
    Task<GetMeResponseDto> GetMeAsync(int userId);
    Task<UpdateMeResponseDto> UpdateMeAsync(int userId, UpdateMeRequestDto request);

    Task<GetUserInterestsResponseDto> GetUserInterestsAsync(int userId);
    Task<UpdateUserInterestsResponseDto> UpdateUserInterestsAsync(int userId, UpdateUserInterestsRequestDto request);

    Task<GetCollectionsResponseDto> GetCollectionsAsync(int userId);
    Task<CreateCollectionResponseDto> CreateCollectionAsync(int userId, CreateCollectionRequestDto request);

    Task<GetCollectionDetailsResponseDto> GetCollectionDetailsAsync(int userId, int collectionId);
    Task<UpdateCollectionResponseDto> UpdateCollectionAsync(int userId, int collectionId, UpdateCollectionRequestDto request);
    Task DeleteCollectionAsync(int userId, int collectionId);

    Task<AddCollectionItemResponseDto> AddCollectionItemAsync(int userId, int collectionId, AddCollectionItemRequestDto request);
    Task DeleteCollectionItemAsync(int userId, int collectionId, int itemId);
}
