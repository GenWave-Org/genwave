# 🎙️ Voice Off results

- Run: take-two
- Date: 2026-10-28
- Script hash: 62d01fd7f1138494b8938aa38a9bf0cdd0ff12f229bed28d31e1ec93f70e89e7
- Hosts: runes-gpu, thor-cpu, thor-gpu

## 🏆 Ranking

The one-star cards (13 to 23) were not ranked against each other, so their order means nothing.

| Rank | Card | Engine | Host | Winces | Stars | Why |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | orpheus | orpheus | runes-gpu | 2 | 4 | Orpheus unquantised (bf16) with its best voice and native sound tags. |
| 2 | vibevoice-7b | vibevoice | runes-gpu | 2 | 4 | VibeVoice's large checkpoint, which can clone the upbeat female clip, to hear what the extra size buys. |
| 3 | fish | fish | runes-gpu | 3 | 4 | Fish's newest checkpoint with its free-form tags, cloned from the upbeat clip. |
| 4 | higgs | higgs | runes-gpu | 3 | 4 | Higgs v3 unquantised (bf16), with its sound tags paired with the written sound they need. |
| 5 | chatterbox | chatterbox | thor-gpu | 5 | 4 | Chatterbox Turbo with its own sound tags and a slightly hotter sampler, cloned from the upbeat female clip. |
| 6 | voxcpm | voxcpm | runes-gpu | 4 | 4 | VoxCPM2 cloned from the upbeat clip with a cheerful style prompt, the control the older release lacked. |
| 7 | chatterbox-original | chatterbox | thor-gpu | 5 | 4 | The original Chatterbox, which trades Turbo's sound tags for the exaggeration knob. |
| 8 | omnivoice | omnivoice | runes-gpu | 5 | 3 | OmniVoice cloned from the upbeat clip with the two sound tags it has. |
| 9 | csm | csm | thor-gpu | 7 | 3 | CSM's only release, cloned through its context from the upbeat clip with the engine's own defaults. |
| 10 | f5-base | f5 | thor-gpu | 8 | 3 | F5 cloned from the upbeat clip with the engine's own defaults; its style comes from the clip alone. |
| 11 | cosyvoice | cosyvoice | thor-gpu | 10 | 3 | CosyVoice 3 with its native sound tags and the text clean-up off, because the clean-up misreads prices and the repeated phone number. |
| 12 | cosyvoice-normaliser-on | cosyvoice | thor-gpu | 11 | 2 | The same CosyVoice 3 setup with its own text clean-up on, to hear what that does to the numbers. |
| 13 | dia | dia | thor-gpu | 8 | 1 | Dia at its native sampler settings with its parenthesised sound tags, cloned from the upbeat clip. |
| 14 | dia2 | dia | runes-gpu | 8 | 1 | The newer Dia2, which lists no sound tags, to hear whether its steadier long reading wins. |
| 15 | kokoro | kokoro | thor-cpu | 12 | 1 | Kokoro at its best: af_heart, its best-graded voice, a touch faster, with the native pause tags for pause, breath and sigh and spoken stand-ins for the sounds it cannot make. |
| 16 | kokoro-as-shipped | kokoro | thor-cpu | 7 | 1 | The reference: the station's own voice and settings, every cue stripped, exactly as it airs today. |
| 17 | parler | parler | runes-gpu | 1 | 1 | Parler's biggest checkpoint with a written description of a bright, upbeat speaker, since a description is its only expressive control. |
| 18 | parler-mini-expresso | parler | thor-gpu | 8 | 1 | The one Parler checkpoint with emotion styles, asked for a happy voice, to hear whether style training beats the bigger model. |
| 19 | piper | piper | thor-cpu | 3 | 1 | Piper's larger model for the same single female voice, with its speed and variation knobs nudged for a livelier read, and no way to make any sound but speech. |
| 20 | qwen3 | qwen3 | thor-gpu | 1 | 1 | Qwen3's voice-design checkpoint asked for an upbeat American woman, since its stock female voices all carry an accent in English. |
| 21 | spark | spark | thor-gpu | 6 | 1 | Spark cloned from the upbeat clip with the engine's own defaults. |
| 22 | styletts2 | styletts2 | thor-gpu | 3 | 1 | StyleTTS2 cloned from the upbeat female clip, with its style knobs pushed toward emotion because it has no tags to lean on. |
| 23 | vibevoice | vibevoice | thor-gpu | 5 | 1 | VibeVoice's small checkpoint with sampling on and more denoising steps, because its stock setting is the flattest it can run. |

## 📝 Notes

A note that is only a time is where the listener stopped the clip.

### orpheus

```
Sounds like the voice is on drugs, but realism, breathing is spot on. Slang and expression is great! Allergies was wrong.
```

### vibevoice-7b

```
Some weirdness in the sound, but might just be a bad render?
```

### fish

No notes.

### higgs

No notes.

### chatterbox

```
Instead of cents it said dents, allergies was wrong and a couple others. Otherwise great voice
```

### voxcpm

```
Speed is a little too fast. Expression is a bit off.
```

### chatterbox-original

```
Messed up dollar amounts and the phone number. Saying GenWave sounded weird. Oddly said allergies fine unlike chatterbox turbo.
```

### omnivoice

```
1m 3s
```

### csm

```
Just... weird. Felt rushed at first, then it seemed like the voice was moving around the room as the levels shifted. Not terrible, just weird.
```

### f5-base

```
46s
```

### cosyvoice

```
Speech seems rushed/forced. Laugh is very forced and unnatural. Phone number is wrong. Slang seems weird, allergies wrong. Whole thing was sort of off.
```

### cosyvoice-normaliser-on

```
Expression is off and feels forced, phone numbers and dollars are off, slang is wrong/off. Sounds robotic, voice is annoying like a mosquito.
```

### dia

```
Just broken, 15+ seconds of screeching at the start, super rushed, no pauses, laugh sounded like a witch on meth, switches to male voice, I didn't even finish the clip. 34s
```

### dia2

```
Dia is out, this was easily as bad or worse than the other Dia. 32s
```

### kokoro

```
Just terrible. 1m 5s
```

### kokoro-as-shipped

```
Slightly less terrible than stock Kokoro. 47s
```

### parler

```
Broken, no voice at all. 17s
```

### parler-mini-expresso

```
27s
```

### piper

```
5s
```

### qwen3

```
3s
```

### spark

```
32s
```

### styletts2

```
6s
```

### vibevoice

```
47s
```

## 🚫 Did not render

Every card rendered.

## 📋 Facts

| Card | Checkpoint | Voices | Cloning | Own voice | Cannot | Tricks | Variant of | Render time |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| chatterbox | Chatterbox Turbo 350M | clone | yes | yes | breath | Laugh, cough, giggle and sigh use Turbo's native tags; Giggle uses the chuckle tag; Pause is written as three plain dots, never the single ellipsis character, which Chatterbox turns into a comma; Turbo ignores exaggeration and cfg weight, so the sampler is the expressive knob | — | 40.9 s |
| chatterbox-original | Chatterbox original 500M | clone | yes | yes | breath, laugh, cough, giggle, sigh | Exaggeration 0.7 with cfg weight 0.3 is the dramatic setting; This checkpoint has no sound tags, so every sound is dropped; Pause is written as three plain dots | chatterbox | 128.0 s |
| cosyvoice | Fun-CosyVoice3-0.5B-2512 with llm.rl.pt | clone | yes | yes | — | Breath, laugh, cough and sigh use the tokenizer's own tags; Giggle shares the laughter tag because there is no softer one; Um is written out as a word; The RL-tuned language model weights (llm.rl.pt) replace the stock ones; The built-in text clean-up is off so digits and dollar amounts reach the model as written | — | 60.0 s |
| cosyvoice-normaliser-on | Fun-CosyVoice3-0.5B-2512 with llm.rl.pt | clone | yes | yes | — | Breath, laugh, cough and sigh use the tokenizer's own tags; Giggle shares the laughter tag because there is no softer one; Um is written out as a word; The RL-tuned language model weights (llm.rl.pt) replace the stock ones; The built-in text clean-up is on, as the engine ships; it turns $2.50 into two point five dollars | cosyvoice | 116.5 s |
| csm | csm-1b | clone | yes | yes | breath, laugh, cough, giggle, sigh | The only expressive lever is the spoken context it is shown; It continues the delivery of the reference clip, so an upbeat clip matters most here; No tags exist, so every sound is dropped | — | 163.4 s |
| dia | Dia-1.6B-0626 | clone | yes | yes | — | Temperature is set to the native 1.2, not the 1.8 that the Hugging Face config would give; Sounds are written in parentheses, Dia's own style; Um is written out as a word; Emotion follows the reference clip | — | 311.4 s |
| dia2 | Dia2-2B | clone | yes | yes | breath, laugh, cough, giggle, sigh | Dia2 lists no sound tags, so every sound is dropped; It handles long text and streams, so drift across lines may be lower | dia | 128.7 s |
| f5-base | F5TTS_v1_Base | clone | yes | yes | breath, laugh, cough, giggle, sigh | The reference clip sets almost all of the style and emotion; Pause is written as an ellipsis, whose extra characters buy extra time; Digits get too little time because duration is estimated from byte count; Weights are non-commercial | — | 33.9 s |
| fish | S2-Pro 4.6B | clone | yes | yes | — | Tags are free-form words in square brackets, so any word is accepted; most cues here use the model card's common tags, and hesitating is a free-form one the model also accepts; Pause uses the model card's [pause] tag; Um is written out as a word; Research-only licence | — | 132.5 s |
| higgs | higgs-tts-3-4b, unquantised (bf16) | clone | yes | yes | — | Sound tags must be attached to an onomatopoeia with no space, so each one carries a written sound after it; Breath is a short pause and pause is the long pause; Um is written out as a word; Weights are non-commercial | — | 63.5 s |
| kokoro | Kokoro-82M v1.0 | 54 | no | yes | — | Pause, breath and sigh use the native [pause:Ns] tag (a kokoro-fastapi v0.6.0 feature, exact spelling only, anything else is read aloud); Sigh is a 0.5 s pause because a written Ahh... is spoken as a word; Laugh, um, cough and giggle have no native tag, so they are written out as spoken words (Haha!, Um, Ahem., Hehe!) | — | 10.7 s |
| kokoro-as-shipped | Kokoro-82M v1.0 | 54 | no | yes | breath, laugh, um, cough, giggle, sigh, pause | Voice blending is possible but not used here | — | 13.3 s |
| omnivoice | OmniVoice 0.6B (current) | clone | yes | yes | breath, cough | Only laughter and sigh match; giggle shares the laughter tag; Voice design from a text description exists but is not used here; Weights are non-commercial | — | 25.7 s |
| orpheus | orpheus-3b-0.1-ft, unquantised (bf16) | 8 | no | yes | breath | Tara is the maker's top voice for realism; Laugh, cough and sigh use native tags; giggle uses the chuckle tag; A higher repetition penalty and temperature make it speak faster; Um is written out as a word | — | 153.0 s |
| parler | Parler-TTS Large v1 (2.2B) | 3 | no | yes | breath, laugh, cough, giggle, sigh | The voice is steered by a written description of the speaker, not by tags; Pace is asked for in the description (slightly fast) because speed is ignored; Um is written out as a word | — | 172.6 s |
| parler-mini-expresso | Parler-TTS Mini Expresso | 4 | no | yes | breath, laugh, cough, giggle, sigh | This is the only Parler checkpoint trained on emotion styles (happy, laughing, whisper and more); The description asks for a happy tone with emphasis; Um is written out as a word | parler | 128.4 s |
| piper | en_US-lessac-high | 1 | no | yes | breath, laugh, cough, giggle, sigh, pause | Speed 1.07 gives a length scale of about 0.93, a little brisker than the default; Noise scale and rhythm variation are raised for a livelier delivery; Um is written out as a word | — | 30.8 s |
| qwen3 | Qwen3-TTS-12Hz-1.7B-VoiceDesign | 0 | no | yes | breath, laugh, cough, giggle, sigh | The voice is designed from a written description, because the stock voices have no native English female; One description covers the whole paragraph; Every sound is dropped | — | 207.6 s |
| spark | Spark-TTS-0.5B | clone | yes | yes | breath, laugh, cough, giggle, sigh | Cloning from the clip is used; its gender, pitch and speed mode cannot be combined with a clip; Output is 16 kHz, which limits the top end; A single call tops out at about 60 seconds, which one paragraph never reaches; Weights are non-commercial | — | 90.4 s |
| styletts2 | StyleTTS2 LibriTTS | clone | yes | yes | breath, cough, giggle | Beta and embedding scale are raised so the emotion follows the text more than the reference clip; Laugh and um are written out as words; Sigh and pause are written as an ellipsis, a sentence break; Backing embedding scale off to 1.5 is the fix if it gets too wild; Speed is ignored; this engine has no speed control | — | 48.7 s |
| vibevoice | VibeVoice-Realtime-0.5B | 6 | no | no | breath, laugh, cough, giggle, sigh | Sampling is turned on and the denoising steps are doubled to 10, away from the flat default; Emma is one of two female voices; Grace is the other and was not picked by ear yet; No tags exist, so every sound is dropped; Very short lines of three words or fewer can be unstable | — | 78.8 s |
| vibevoice-7b | VibeVoice 7B (community mirror), loaded 4-bit | clone | yes | yes | breath, laugh, cough, giggle, sigh | The big checkpoint can clone a voice; the 0.5B one cannot; The weights come from a community copy of the model Microsoft took down, and they are squeezed to 4-bit so they fit on a 16 GB card; No tags exist, so every sound is dropped | vibevoice | 72.4 s |
| voxcpm | VoxCPM2 (about 8 GB) | clone | yes | yes | breath, laugh, cough, giggle, sigh | VoxCPM2 takes a short style prompt in front of the text for a controlled clone; Its own text normaliser is off so digits arrive as written; Bad takes are retried automatically; No tags exist, so every sound is dropped | — | 80.9 s |
