# Merge Report: master into fix/hardening

## Summary

- Current branch: `fix/hardening`
- Master commit merged: `origin/master` at `1ecd995f`
- Backup branch created: `backup/fix-hardening-before-master-merge`
- Safety stash created before merge: `stash@{0}` named `pre-master-merge-safety-fix-hardening`
- Merge command used: `git merge origin/master --no-commit --no-ff`
- Merge commit created: No. The merge is resolved and staged, but not committed because full requested test verification is not clean.
- Push performed: No.

## Initial State

- Branch confirmed as `fix/hardening`.
- The tracked worktree was clean before the merge, but there were many untracked local report/document files.
- Untracked state was preserved with a named safety stash before fetching and merging.
- `origin/master` was fetched before merge.

## Conflicted Files

1. `admin-app/src/app/features/add-pages/pages/add-event/add-event.component.ts`
2. `admin-app/src/app/features/add-pages/pages/add-place/add-place.component.html`
3. `admin-app/src/app/features/add-pages/pages/add-place/add-place.component.ts`
4. `admin-app/src/app/features/events/pages/event-detail/event-detail/event-detail.component.html`
5. `admin-app/src/app/features/places/pages/place-detail/place-detail/place-detail.component.html`
6. `server/Geotrack/Geotrack.Application/Interfaces/Service/IPlaceContributionService.cs`
7. `server/Geotrack/Geotrack.Infrastructure/Services/PlaceContributionService.cs`
8. `server/Geotrack/Geotrack.Infrastructure/Services/PlaceService.cs`
9. `server/Geotrack/Geotrack.Infrastructure/Services/RegionalDestinationService.cs`
10. `server/Geotrack/Geotrack.Infrastructure/Services/RegionalEventService.cs`
11. `server/Geotrack/Geotrack.Infrastructure/Services/RegionalLocationService.cs`
12. `server/Geotrack/Geotrack.Infrastructure/Services/RegionalPlaceService.cs`
13. `server/Geotrack/Geotrack.IntegrationTests/ContributionPublicationTests.cs`
14. `server/Geotrack/Geotrack.Server/Controllers/AdminRegionalController.cs`
15. `server/Geotrack/Geotrack.Server/Controllers/PlacesController.cs`
16. `user-app/src/app/features/contributions/pages/my-contributions/my-contributions.component.html`
17. `user-app/src/app/features/contributions/pages/submit-contribution/steps/step-preview/step-preview.component.spec.ts`
18. `user-app/src/app/features/contributions/services/contributions.service.spec.ts`
19. `user-app/src/app/features/hierarchy-detail/pages/entity-hierarchy-detail-page/entity-hierarchy-detail-page.component.ts`
20. `user-app/src/app/features/hotel-detail/pages/hotel-detail-page/hotel-detail-page.component.ts`
21. `user-app/src/app/features/place-detail/pages/place-detail-page/place-detail-page.component.ts`

## Resolution Summary

- Admin add-event: preserved hardening draft/edit image handling and master gallery/image handling. Added the missing `ClassItem` interface needed by the merged component.
- Admin add-place: preserved hardening cover crop/edit-mode warning and master time field/gallery form additions.
- Admin detail HTML: kept hardening sidebar/layout fixes and master feedback/amenity sections without duplicating classification blocks.
- `IPlaceContributionService`: kept hardening approve/reject audit parameters and added master content-moderator, option, region, and image APIs.
- `PlaceContributionService`: combined hardening moderation/audit/sanitizer behavior with master full contribution payload, content-moderator review, operating hours, stay types, and image helper behavior.
- `PlaceService`: combined hardening owner update audit/sanitizer behavior with master multipart owner update image handling and structured operating hours updates.
- Regional services: kept hardening rich-description sanitization and added master `RegionalTagWriter` tag persistence.
- `AdminRegionalController`: kept hardening regional image storage, upload limit, and cancellation-aware upload flow while removing duplicate injected storage.
- `PlacesController`: kept master multipart owner update endpoint and hardening contribution rate limiting.
- Integration tests: kept hardening proof/audit coverage and master content-manager ownership update coverage, then updated fakes for new service methods.
- User contribution specs: aligned tests to merged full-payload, dynamic stay-type, image, proof-warning, and feature-option behavior.
- User hierarchy/hotel/place detail components: preserved hardening analytics/avatar imports and master shared gallery/description-rich-html imports.
- Content moderator specs: added the new contributions service mock and rich HTML pipe import needed by merged pages.

## Fix/hardening Functionality Preserved

- Auth/session and role-sensitive test fakes were kept intact where touched.
- Owner update audit metadata, internal notes, source areas, and sanitizer use were preserved.
- Regional add/edit image upload hardening, size limits, and cancellation handling were preserved.
- Public visibility and content access patterns were not intentionally weakened.
- Regional detail/sidebar layout and classification placement were preserved.
- Review/profile/avatar related imports and UI fixes were preserved in touched user detail pages.
- Contribution ownership payload hardening was preserved and combined with master expanded payload fields.
- Backend upload/rate-limit protections were preserved on owner update endpoints.

## Master Functionality Preserved

- Shared description formatting and `descriptionRichHtml` pipes were added to user-app and admin-app.
- Shared user-app `PhotoGalleryComponent` was added and wired into detail/contribution flows.
- Place, event, hotel, and hierarchy detail modernization was retained, including rich descriptions, collapse behavior, cover-first galleries, and gallery buttons.
- Opening-hours structured data and overnight/24-hour display support were retained.
- Map stacked marker behavior was retained.
- Submit contribution stay-type loading, geocoding, image reselection/crop reset, gallery preview, proof lock, and full payload behavior were retained.
- Contribution detail gallery, proof replacement, PUT proof update, and full payload behavior were retained.
- Content Moderator contribution list/detail pages, sidebar entry, dashboard updates, and route guards were retained.
- Backend `DisplayTextSafety`, `RegionalTagWriter`, expanded `PlaceContributionService`, and expanded `PlaceService` behavior were retained.

## Migrations and Model Snapshot

- No migration files were deleted.
- Master migrations added and staged:
  - `20260601120000_AddDescriptionToTerritoryAndRegion`
  - `20260604150000_AddOperatingHoursToPlaceContributions`
  - `20260606120000_AddCityRegionCountryToPlaceContributions`
  - `20260608211413_Update`
- `AppDbContextModelSnapshot.cs` was merged and builds with the combined model.
- Schema additions preserved include Region/Territory descriptions and PlaceContribution operating hours, city, region, and country fields.

## Production Auto-migrate/Seed Check

- `server/Geotrack/Geotrack.Server/Program.cs`: no staged diff.
- `server/Geotrack/Geotrack.Server/appsettings.Production.json`: no staged diff.
- `server/Geotrack/Geotrack.Server/SeedData.cs`: changed by master seed content and stay-type/public-content additions; no manual conflict edits were made to production startup flow or production flags.
- Verified flags/calls:
  - `appsettings.Production.json` still has `Database:AutoMigrate` set to `true`.
  - `EnableOperationalSeed` remains `true`.
  - `EnablePublicContentSeed` remains `true`.
  - `RunPublicContentInProduction` remains `true`.
  - `Program.cs` still performs `MigrateAsync`, `SeedData.InitializeAsync`, and `SeedData.EnsureRequiredPublicContentAsync` under the existing startup conditions.

## Verification

- `git diff --check`: passed.
- Repository-wide conflict-marker scan: no conflict markers found.
- `dotnet build server/Geotrack/Geotrack.sln`: passed after updating test fakes for new merged service methods.
- Backend targeted tests:
  - `dotnet test server/Geotrack/Geotrack.sln --no-build --filter "FullyQualifiedName~Auth"` timed out with the local runner producing no useful result.
  - `dotnet test server/Geotrack/Geotrack.IntegrationTests/Geotrack.IntegrationTests.csproj --no-build --filter "FullyQualifiedName~Contribution"` timed out with the local runner producing no useful result.
  - Remaining backend filters were not run after repeated silent test-runner timeouts.
- `user-app` build: passed. Warnings: existing style budget warnings for detail SCSS and MapLibre CommonJS warning.
- `user-app` focused contribution tests: passed, 9 files and 34 tests.
- `user-app` full `npm.cmd run test:ci`: failed, 5 files and 11 tests. Failures are outside the contribution merge slice and include Explore URL expectation changes, Interests skip route expectation, Login expression-changed error, Explore page stale empty-state expectations, and a TestBed contamination failure after earlier Explore failures.
- `admin-app` build: passed. Warning: initial bundle exceeded the configured 500 kB budget.
- `admin-app` focused content-moderator/add-event tests: passed, 3 files and 12 tests.
- `admin-app` full `npm.cmd run test:ci`: failed. Failures remain in broader/stale specs including user-management copy expectations, AuthService mocks missing `isContentModerator`, national dashboard/analytics, add-region, region detail, region map TestBed setup, and all-regions image/visibility expectations.
- Package files did not change, so `npm audit --audit-level=low` was not required by the requested condition.

## Manual QA Checklist

Manual browser QA was not completed because the merge is intentionally left uncommitted while full verification is failing. Required follow-up checks before commit/deploy:

- User-app Place detail: rich description, show more/less, gallery button, cover-first gallery, opening-hours edge cases, reviews/avatars.
- User-app Hotel/Event detail: gallery behavior, description formatting, booking partner layout, no old thumbnail hero switching.
- User-app hierarchy detail: fixed top/back bar, Region/Territory descriptions, destinations showing locations only.
- User-app map: stacked same-coordinate markers, stack picker close behavior, regular marker navigation, route/direction behavior.
- Submit contribution: server stay types, image reselection/crop reset, gallery preview, auto-geocode, proof lock, full payload.
- Contribution detail: shared gallery, proof replacement modal, PUT proof endpoint, full payload preservation.
- Ask Agent/adaptive search: public search behavior and no provider/key leakage.
- Admin Content Moderator: dashboard, entity review pages, contribution list/detail pages, single sidebar entries, role guards.
- Notifications: drawer All/Unread and skeleton state.
- Regional/Admin entity details: sidebar layout, map/tags order, content visibility.
- Analytics: destination/location rollups where touched.
- Backend: migrations compile, ownership payload, tags, proof replacement, public visibility, sanitization, upload/rate limits, search document sync.

## Known Limitations

- Merge conflicts are resolved and staged, but the merge is not committed because the requested full verification did not pass.
- The frontend full-suite failures appear largely concentrated in pre-existing or broader stale specs rather than the focused merge areas, but they still block the requested "commit only if everything passes" policy.
- Backend build passes, but targeted backend tests timed out in the local runner.

## Final Status at Report Creation

- Conflicts: all resolved.
- Staged merge changes: 159 files changed before adding this report.
- Merge commit: not created.
- Push: not performed.
