# Settings UI references

The user's 2026-11-10 attachment adds these references to the existing UI reference list.
Keep the agreed dark navy / blue palette, compact spacing, 4–8px corner radii, aligned text,
round switch knobs and visible scrollbars. Use purposeful hover/focus feedback.

| Reference | URL | Inspection / use in this change |
|---|---|---|
| Looseleaf | https://kevintngo.github.io/looseleaf/ | Official GitHub README checked: https://github.com/kevintngo/looseleaf . Semantic controls, consistent tokens and visible focus are useful references. |
| Rare UI | https://rareui.com/ | Official component-library page checked; reference for restrained interaction polish. No source copied. |
| ExpoKit | https://expokit.in/docs/components | Official catalog checked, especially Slider and Switch: thin tracks, round thumbs, clear interaction states and shared theme tokens. |
| goopUI | https://goop.timmcmasters.work/docs/components | User-supplied reference; page could not be retrieved in this environment. Specific components not verified. |
| beUI | https://beui.dev/ | Official library / motion catalog checked; reference for a consistent component family. No source copied. |
| Sketchbook UI | https://sarthakrawat-1.github.io/sketchbook-ui/ | User-supplied reference; page returned no readable component content. Specific components not verified. |
| AffectUI | https://www.affectui.com/ | Identified through the official https://github.com/flohoeller/affectui repository. Catalog describes coordinated states / dark theme. No source copied. |

Applied here: consistent6px button/input/value-bubble radii; a slim blue focus ring around the slider
thumb; centered value text; analytic antialiasing on small curves. Existing settings, explanations,
save/apply behavior and motions are retained. This is a component-level refinement, not a new layout proposal.

The AA implementation is original C#/GLSL in Graphics/SmoothShapes.cs. It is not an imported React,
React Native or CSS library. It does not supersample the entire game or enlarge UI text.
