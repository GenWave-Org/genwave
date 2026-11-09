# 🎙️ Patter bench

- Endpoint: http://runes:11434/
- Breaks per row: 60
- Run at: 2026-11-09 17:54 UTC

| Model | Persona | Distinct 4-grams | Top repeats | Catchphrase rate | Gate pass | p50 ms | p95 ms | VRAM GiB |
|---|---|---|---|---|---|---|---|---|
| mistral:7b | flip | 0.45 | "with captain flip and" ×47; "youre with captain flip" ×47; "and the next track" ×46; "captain flip and the" ×46; "flip and the next" ×46 | 0.00 | 1.00 | 658 | 868 | 5.2 |
| mistral:7b | sal | 0.66 | "comes a tune thats" ×42; "here comes a tune" ×42; "a tune thats bound" ×41; "tune thats bound to" ×41; "alright listeners here comes" ×38 | 0.00 | 1.00 | 672 | 780 | 5.2 |
| mistral:7b | tony | 0.67 | "ballroom of the airwaves" ×57; "of the airwaves with" ×42; "swingsters gather round the" ×26; "the airwaves with their" ×24; "big tonys ballroom of" ×20 | 0.00 | 1.00 | 704 | 1435 | 5.2 |
| gemma4:12b | flip | 0.09 | "are hitting maximum velocity" ×60; "the flight plan is" ×60; "we are hitting maximum" ×60; "flight plan is screaming" ×59; "hitting maximum velocity with" ×59 | 0.02 | 1.00 | 827 | 955 | 7.8 |
| gemma4:12b | sal | 0.20 | "to the day then" ×60; "is soaring as we" ×55; "soaring as we head" ×54; "energy is soaring as" ×53; "the energy is soaring" ×53 | 0.00 | 1.00 | 932 | 1082 | 7.8 |
| gemma4:12b | tony | 0.30 | "ballroom of the airwaves" ×60; "the ballroom of the" ×60; "into the ballroom of" ×59; "of the airwaves for" ×59; "right into the ballroom" ×59 | 0.02 | 1.00 | 876 | 1108 | 7.8 |
| gemma3:12b | flip | 0.10 | "alright genwave listeners were" ×60; "best hour weve ever" ×60; "genwave listeners were locked" ×60; "hour weve ever done" ×60; "in three two one" ×60 | 0.00 | 1.00 | 893 | 1110 | 7.8 |
| gemma3:12b | sal | 0.36 | "then to the day" ×60; "to the day then" ×60; "friends next up its" ×26; "friends next we have" ×12; "next up its the" ×11 | 0.00 | 1.00 | 633 | 797 | 7.8 |
| gemma3:12b | tony | 0.24 | "get ready for something" ×57; "lets get ready for" ×57; "ready for something real" ×57; "hello there music lovers" ×55; "lovers lets get ready" ×55 | 0.02 | 1.00 | 838 | 1159 | 7.8 |
| qwen3:14b | flip | 0.43 | "flight plan into that" ×52; "the flight plan into" ×52; "lets send the flight" ×46; "send the flight plan" ×46; "about to take you" ×23 | 0.00 | 1.00 | 888 | 1124 | 9.8 |
| qwen3:14b | sal | 0.40 | "to the day then" ×60; "a track thats been" ×58; "heres a track thats" ×57; "track thats been waiting" ×37; "thats been waiting for" ×32 | 0.00 | 1.00 | 865 | 1109 | 9.8 |
| qwen3:14b | tony | 0.23 | "ballroom of the airwaves" ×60; "the ballroom of the" ×60; "airwaves the lovebirds out" ×59; "and gentlemen step right" ×59; "are in for a" ×59 | 0.00 | 1.00 | 1046 | 1247 | 9.8 |
| mistral-nemo:12b | flip | 0.56 | "alright genwave captain flip" ×57; "genwave captain flip here" ×57; "incoming in three two" ×51; "captain flip here get" ×13; "flip here get ready" ×13 | 0.00 | 1.00 | 494 | 777 | 7.7 |
| mistral-nemo:12b | sal | 0.51 | "afternoon genwave sunrise sal" ×50; "genwave sunrise sal here" ×50; "good afternoon genwave sunrise" ×50; "to the day then" ×20; "sunrise sal here the" ×17 | 0.00 | 1.00 | 555 | 685 | 7.7 |
| mistral-nemo:12b | tony | 0.75 | "ladies and gents the" ×50; "and gents the ballrooms" ×49; "big tony marconi here" ×19; "and were about to" ×15; "here and were about" ×15 | 0.00 | 1.00 | 713 | 900 | 7.7 |
| llama3.1:8b | flip | 0.55 | "the flight plans got" ×57; "flight plans got a" ×56; "were leveling off at" ×20; "crew were leveling off" ×15; "leveling off at cruising" ×14 | 0.00 | 1.00 | 373 | 468 | 5.4 |
| llama3.1:8b | sal | 0.61 | "and i just love" ×28; "i just love how" ×28; "love how this next" ×28; "just love how this" ×27; "this next ones gonna" ×27 | 0.00 | 1.00 | 456 | 680 | 5.4 |
| llama3.1:8b | tony | 0.52 | "ballroom of the airwaves" ×55; "the ballroom of the" ×52; "back to the ballroom" ×45; "to the ballroom of" ×45; "welcome back to the" ×45 | 0.02 | 0.98 | 615 | 828 | 5.4 |

## ✅ The pick

Dean picked `mistral-nemo:12b` on 2026-11-09. It was the most varied on average (distinct 4-grams 0.61 vs 0.59 for the current `mistral:7b`). It costs 7.7 GiB of VRAM, up from 5.2 GiB.

- Runes ran with `OLLAMA_CONTEXT_LENGTH=8192`, and the station was off during the run, so nothing else used Ollama.
- Every break used the same station clock. Time-of-day lines such as "to the day then" repeat more here than they would on air.
- Gate pass is about 1.00 for every model, because bench breaks carry no fact block. It doesn't separate the models.
- Catchphrase rate is about 0.00 for every model. Quirks are picked rarely, and models seldom use the one they get.
- The repeat check (T693) runs after the model replies, and the bench doesn't include it. These are the raw model numbers.
