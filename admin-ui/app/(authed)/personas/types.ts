/** Wire shape of one row from `GET/POST/PATCH /api/personas` (SPEC F35.4). `voice: ""` is the
 * station-default sentinel — the same convention `Station:Voice`/the F27 safe-segment `voice`
 * field already use. */
export interface PersonaDto {
  id: number;
  name: string;
  backstory: string;
  style: string;
  voice: string;
}
