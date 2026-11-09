# 🎙️ PatterBench

Picks a model by how varied it sounds. For each model and each DJ, it asks for N breaks through the real prompt builder, with the DJ's recent lines in the prompt as on air. It runs each reply through the truth gate and writes one Markdown report. It never touches the station or its database.

## ▶️ Run it on runes

```bash
dotnet run --project tools/PatterBench -- --endpoint http://runes:11434 --models mistral:7b,gemma3:12b --personas flip,sal,tony -n 60
```

- `--endpoint` is the base URL of an OpenAI-style server (Ollama works).
- `--models` and `--personas` are comma lists. Personas default to `flip,sal,tony`.
- `-n` is breaks per row (default 60). 4 models × 3 DJs × 60 breaks is about 720 calls, roughly 40 minutes.
- `--out` is the report file (default `tools/PatterBench/report-<date>.md`).

If the endpoint cannot be reached, it prints why, exits with 1 and writes no report.

## 📊 Reading the table

One row per model and DJ.

| Column | Meaning |
|---|---|
| Distinct 4-grams | Share of four-word runs that are not repeats. Higher is more varied. |
| Top repeats | The five four-word phrases found in the most breaks, with how many breaks. |
| Catchphrase rate | Share of breaks that say the DJ's once-a-shift line. `n/a` if the DJ has none. Near 1.00 means the model ignores the cap. |
| Gate pass | Share of breaks that pass the truth gate on the first try. |
| p50 ms, p95 ms | Typical and slow call time. |
| VRAM GiB | Video memory the model holds once loaded, from Ollama's `/api/ps`. `n/a` if the server does not say. |

The DJ cards in `personas/` are copies of the catalog's Captain Flip, Sunrise Sal and Big Tony Marconi.
