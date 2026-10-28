# Step 8 — firmware + docs

Firmware in PlatformIO with the logic core as plain C/C++ under native host
tests (for example: a command parser, a USB session layer, a bus simulation
— no hardware needed to test). `docs/bom.md` covers the whole unit including
off-board parts; `assembly.md` covers build order, enclosure checks (get the
case first, check the panel outline against its real panels) and bring-up
checks; keep README's regeneration command list exact.

