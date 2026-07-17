/** Shared DTO for a library row returned by GET /api/libraries. */
export interface LibraryDto {
  id: number;
  name: string;
  mediaCount: number;
}
