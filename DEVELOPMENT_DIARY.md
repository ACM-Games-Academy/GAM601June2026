# Hieroglyph — Development Diary

A week-by-week devlog reconstructed from the real commit history of this
repository (branches `main`, `Font-Surgery`, and `YarnSpinnerTryout`
combined, 82 commits total, May 24 – July 28, 2026). No GitHub Issues or
Pull Requests were pulled in — the `gh` CLI isn't installed in this
environment — and there are no tags marking milestones, so this is built
from commit messages, timestamps, and file-level diffs alone.

---

## Week of May 24–31, 2026 — Getting Something on Screen

Day one. I started the Unity project, and on that very first day I'd
already sketched out `GridManager` — the script that would go on to run
the wordsearch puzzle grid for the rest of the project — and imported the
Pixel Crusher Dialogue System as my first attempt at a dialogue engine.

The next day I built out actual dialogue "nodes" and wired up a
wordsearch trigger node inside that system, and by the end of the day I
had a first working prototype. That's a genuinely good opening 48 hours —
core puzzle scaffolding and a functioning (if rough) dialogue trigger in
place before the first weekend was even over.

---

## Week of June 1–14, 2026 — Fonts and a Quiet Stretch

This entry covers two calendar weeks together, because the real activity
in them was thin and separated by a roughly week-long gap (June 3–8) with
no commits at all.

On June 2nd I got a "fallback font" working, at least partially — the
commit message hedges with "sortof," so I'm not overselling it: this was
clearly an early, imperfect attempt at handling font rendering, likely
for the hieroglyph text. After the gap, I picked back up on June 9th (the
commit message for that one is just the date, so there's nothing more
specific to say about it) and started using a TMP (TextMeshPro) font
converter asset — a tool for turning a font into the SDF format Unity's
text system needs. June 10th was spent adding and arranging background
art ("Backgrounds in an okay sort of order," by my own admission), capped
off with an "End of Wednesday Session" commit. Two of that day's commits
are just GitHub Desktop's own auto-generated branch-sync bookkeeping
(`On New-Attempt-at-Background: ...`), not real content — I'm noting that
plainly rather than inventing meaning for them.

The most consequential thing in this stretch happened on June 12th:
Yarn Spinner installed. In hindsight this is the moment the project's
dialogue system started to pivot away from the Pixel Crusher plugin from
week one, toward what it actually runs on today.

---

## Week of June 15–21, 2026 — Portraits and the First Puzzle Loop

Every commit in this entry happened in a single day, June 20th — six
commits back to back, reading like one long session. I made the UI panel
look better, then built out the Character Portrait System (the slotted
portrait display that's core to how dialogue looks today), got
background and portrait art working together, and implemented the
wordsearch answer appearing and hiding — i.e., the actual mechanic of a
correct answer revealing itself and the puzzle then getting dismissed.
One commit is just labeled "going to experiment," which is honestly a
WIP marker rather than a description of finished work, so I'm leaving it
at that.

---

## Week of June 22–28, 2026 — Puzzle Content Tools and Affinity Tracking

Two productive days. On the 23rd I implemented the `.yarn` puzzle-making
system — the mechanism that lets a `.yarn` dialogue file declare a
wordsearch puzzle's words and answers directly, rather than hand-coding
each one. On the 24th I started and finished the Affinity Tracker in the
same day: the system that tracks the player's standing with each god and
human character based on the choices they make through the puzzles.

---

**No commits between June 25th and July 7th.** That's a clean two-week
gap — the longest silent stretch in the whole project history. I don't
have a reliable way to say why from the commit log alone, so I'm not
going to guess.

---

## Week of July 6–12, 2026 — Font Surgery and the Name Tab

A dense week, and the one that explains the `Font-Surgery` branch name
still checked out in this repo today. On July 8th I made a "Quick save"
(the commit swept in a large Unity auto-recovery file alongside the real
scene changes — this reads like a just-in-case checkpoint rather than a
planned commit), built a scanner/balance tool for the affinity system,
replaced placeholder character names with real ones, and restructured the
portrait-switching code.

July 10th was the font migration in earnest: installing a new font,
switching to NewGardiner (a proper Egyptological hieroglyph font family)
in place of whatever was rendering hieroglyphs before, and continuing
that "surgery" through several follow-up passes on the font asset itself.
The same day I also built the sliding name tab — the UI element that
slides across to sit above whichever character is currently speaking —
and got the slide animation itself working by the end of the day.

---

## Week of July 13–19, 2026 — The Big Content and Polish Push

The single richest week in the project's history — 21 commits across
five days.

July 13–14 were mostly content: writing in the "duat 1" and "temple 1"
dialogue sections, getting Cat_Meritamun's paw-raised expression working,
adding brightness feedback when hovering the puzzle grid, and finishing
that round of dialogue input. July 15th added a custom cursor, a
click-to-skip feature for the dialogue box's typewriter text effect, and
a fix for a portrait transparency bug where portraits were visibly
overlapping incorrectly.

July 16th was a marathon — eleven commits in one day. I built the glow
effect that plays when a puzzle is answered correctly (started and
finished the same day), added a way to deselect a puzzle answer, refined
the name tab's bounce and sliding curve, fixed a visual bug in the
dialogue box's decorative bronze brackets, built the full Meritamun →
Cat_Meritamun crossfade transformation scene, added sound effects, added
the splash screen and title fonts, added scuff sound effects, and put in
the whole music system. July 17th followed up with night-time music and
the anger reaction effect (the shake-plus-red-pulse beat characters get),
including a same-day fix to its visuals once it wasn't looking right.

---

## Week of July 20–26, 2026 — Reactions, Emotes, and a Harder Puzzle

Another very active week — 20 commits across four days.

July 20th refined the "wrong answer" reaction, added the cat's purring
sound and a happy meow cue, and added red question-mark visuals for
incorrect guesses. One commit that day is just labeled "bug fix" with no
further detail in the message — I don't know specifically what it
addressed, so I'm not going to guess. July 21st added Cat_Meritamun's
standing animation pose, inserted new puzzle content ("new meanings
inserted" — most likely new hieroglyph answers/translations, based on
the dialogue file it touched, but I'm inferring that from context rather
than a message that says so outright), and built the exit-to-menu button.

July 22nd added a "puzzled" thinking expression, audio ducking (the music
dipping under sound effects), a fix to the puzzle grid's background, the
portrait "pop" scale animation and an eased version of it, adjustments to
how dim/inactive portraits are tinted, and a large ambience pass across
both the scene and `BackgroundManager` — this is the day/night atmosphere
work (the beginnings of what later became torches, clouds, and the god
ray effects). July 23rd wrapped the week with more puzzle audio work,
a change that deliberately increases how many decoy hieroglyphs get
mixed into the puzzle grid (making it harder to spot the real answer), a
new 9×9 "hard mode" grid size, matching cell padding for that bigger
grid, and a god rays lighting prefab.

---

## Week of July 27 – August 2, 2026 — Splash Screen Life *(in progress)*

Only one day in so far (July 28th), and this week isn't over yet as of
today — so treat this entry as a partial one. It added cycling portrait
sprites to the splash screen, a small animated goldfish effect for the
pond, red-tinted highlighting in dialogue text, a new "sad" reaction
emote animation, and a fix described only as a "sound bug" — the diff
touches the dialogue file rather than the music code, so I can't say
precisely what the audio issue was without more context than the message
gives.

---

## Reflections

A few patterns stand out across the full history:

**The project started slow and got much busier near the end.** May and
June show long gaps — six days here, a full week there, and one clean
two-week silence between June 25th and July 7th. From July 8th onward,
activity is dense almost every week, culminating in the two heaviest
weeks of the whole project (July 13–19 and July 20–26, 21 and 20 commits
respectively).

**Hieroglyph font rendering was a recurring struggle, not a one-time
fix.** It shows up as "fallback font" in early June, a TMP converter tool
a week later, and then a dedicated multi-day "Font Surgery" push in
mid-July that switched to a proper Egyptological font family. Getting
text to render correctly clearly took more than one pass.

**The dialogue system was replaced outright, not iterated on.** The
project started on the Pixel Crusher Dialogue System on day one, then
installed Yarn Spinner three weeks later — a full swap of the underlying
tool the whole game is built on, not a small tweak.

**The portrait/name-tab system was revisited repeatedly.** It's built in
June ("Character Portrait System"), restructured in early July ("improved
and restructured the portrait switcher"), and then refined again in late
July with pop animations, easing, and alpha tinting. This reads as a
feature that kept getting good enough to move past, then getting revisited
once its rough edges became annoying.

**Character reaction effects cluster almost entirely in the last two
weeks.** Anger pulse, purring, happy meow, puzzled expression, portrait
pop, and the sad reaction animation are all packed into July 16th onward —
this is clearly where the "emotive reaction" side of the game took shape,
quite late relative to the rest of the project.

**Puzzle difficulty was tuned more than once.** The core `.yarn`
puzzle-making system went in on June 23rd; it wasn't until July 23rd that
its difficulty got deliberately increased (more decoy glyphs) and a
harder 9×9 grid mode was added — a full month later, suggesting this was
balanced only after the rest of the game existed to balance it against.

**A handful of commits are housekeeping, not features**, and I've treated
them that way rather than writing invented narrative around them: two
GitHub Desktop auto-sync commits on June 10th, an auto-recovery file
swept into a "Quick save" on July 8th, and a small number of commit
messages ("bug fix," "Quick save," "going to experiment," "June 9th")
too terse to reconstruct real detail from.
