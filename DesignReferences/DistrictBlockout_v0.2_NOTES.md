# District Blockout v0.2 — Reference Notes

This document defines how to interpret:

`DesignReferences/DistrictBlockout_v0.2.png`

The PNG is the final spatial layout reference.

This Markdown file is the interpretation constraint.

If the image is visually ambiguous, follow the rules written here.

---

## 1. Purpose

This reference defines the target town blockout layout for the `DailyLife.unity` rebuild.

This is not a moodboard.

This is not a loose inspiration image.

This is a spatial planning reference that defines:

- district structure
- relative building placement
- road structure
- sidewalk structure
- major pedestrian crossing points
- entrance orientation
- open spaces
- decorative building density
- street furniture distribution

Exact meter-perfect proportions are not required.

The relative relationships and functional logic are important.

---

## 2. Current Development Goal

The purpose of District Blockout v0.2 is to replace the current prototype-style town layout with a denser and more believable small-town outskirts district.

The new layout should support:

- the existing Day 1 Daily-Life loop
- future Day 2+ story content
- clearer landmark recognition
- believable walking routes
- future vehicle traffic
- future traffic light / pedestrian crossing systems
- future environmental art / texture passes

This pass should prioritize world structure before final visual quality.

---

## 3. Overall Town Structure

The district should feel like a compact small-town outskirts area.

The main structure consists of:

- Main Street
- secondary streets
- Side Street
- Alley / Back Lane
- Back Alley
- Delivery Alley
- residential blocks
- commercial blocks
- workplace / service area
- public leisure spaces
- parking area

The world should no longer feel like one long test road with important buildings lined up beside it.

It should read as several connected town blocks.

---

# 4. Main District Zones

## Zone A — Residential / Home-Life Zone

Main northern residential area.

Includes:

- Villa
- Grocery Store
- nearby Facade buildings
- Apartment
- Alley / Back Lane
- residential sidewalks

This is the player's home-life anchor.

---

## Zone B — Home-Adjacent Commercial / Landmark Zone

Located across Main Street from the Villa-side district.

Includes:

- Game CD Shop
- Small Open Lot
- General Mart
- nearby Facade buildings

This area should remain visually connected to the player's home district.

The Game CD Shop is especially important as a recurring story landmark.

---

## Zone C — Mid Commercial Zone

Located south / southwest of Main Street.

Includes:

- Cafe
- Pub / Bar
- Fast Food Store
- Facade buildings
- Side Street
- Back Alley
- nearby Parking

This area represents optional town-life content and social / commercial activity.

---

## Zone D — Civic / Leisure Zone

North-east area.

Includes:

- Apartment
- Park
- Playground
- nearby Facade building

This zone mainly supports:

- neighborhood identity
- visual relief
- greenery
- public-space density

---

## Zone E — Workplace / Service Zone

South-east area.

Includes:

- Convenience Store
- Workplace
- Delivery Alley
- Smoking Area
- nearby Facade buildings
- service roads

This is the daily work-life anchor.

---

# 5. Building Classification

Buildings should be interpreted in two categories.

## A. Gameplay / Potential Interior Buildings

These buildings either already contain interaction content or are intended to support future playable content.

- Villa
- Grocery Store
- Game CD Shop
- General Mart
- Cafe
- Pub / Bar
- Convenience Store
- Workplace
- Fast Food Store

Some of these may remain Blockout-only during the current pass.

Do not automatically build full interiors unless specifically requested.

---

## B. Facade Buildings

Any building labeled:

`FACADE`

is a decorative / background density building.

Facade means:

- no required gameplay interior
- no required shop system
- no required story content
- no required NPC
- no required interaction system

A Facade may still have:

- a visible front door
- windows
- signage later
- exterior props

but should remain non-playable unless the user explicitly upgrades it later.

Do not invent gameplay content for Facade buildings.

---

# 6. Building-Specific Rules

## 6.1 Villa

The Villa is the player's home.

Requirements:

- northern residential zone
- visually important home-life landmark
- Grocery Store remains nearby
- access should remain easy from Main Street and local residential streets

Preserve the existing functional interior where possible.

---

## 6.2 Grocery Store

Grocery Store remains near the Villa.

Purpose:

- residential daily-life shop
- food / grocery-oriented building
- convenient home-area access

Do not move it into the workplace district.

---

## 6.3 Game CD Shop

Game CD Shop is a major story landmark.

Requirements:

- must remain across Main Street from the Villa-side district
- should not be directly attached to the Villa block
- should be slightly offset rather than perfectly centered
- must still be visually noticeable from the Villa-side daily route
- future open / closed state should be readable naturally during daily movement

Do not move it far away from the Villa area.

---

## 6.4 General Mart

General Mart is now separate from the Convenience Store.

Final role:

- household goods
- tableware
- general products
- miscellaneous daily goods

Location:

- north-middle commercial row
- right side of Small Open Lot
- Main Street-facing building

Its front entrance should face Main Street.

Important:

The existing old General Mart gameplay system should NOT remain conceptually attached to this new General Mart during this blockout pass.

The old interactive General Mart will be converted into the Convenience Store near Workplace.

The new General Mart can initially remain a Blockout building without full interior implementation.

---

## 6.5 Convenience Store

Convenience Store is the building immediately left of Workplace.

This is the store used in the Day 1 lunch sequence.

It replaces the old General Mart role.

The existing interactive General Mart should be moved / renamed / converted into this Convenience Store.

This includes updating relevant:

- store identity
- storeId
- Day 1 lunch references
- Mike route references
- Story Zones
- Checkout references
- Store Exit story conditions
- Food-purchase checks

Do not leave Day 1 lunch logic pointing to `GeneralMart`.

The final gameplay identity should be:

`Convenience Store`

---

## 6.6 Cafe

Cafe belongs to the mid-commercial zone.

Requirements:

- clearly visible from Main Street
- not hidden behind another building
- accessible from normal pedestrian circulation
- remains optional Daily-Life content

---

## 6.7 Pub / Bar

Pub / Bar belongs to the mid-commercial zone.

Requirements:

- clearly visible from Main Street
- should read as a recognizable social building
- optional playable / future content building
- front entrance faces Main Street

The final map explicitly shows its Front Entrance on the Main Street side.

Do not hide the entrance in Back Alley.

---

## 6.8 Fast Food Store

Fast Food Store is part of the mid-commercial block behind the Pub / Bar area.

Purpose:

- optional future town content
- visual / functional commercial variety

It may remain Blockout-only for now.

Do not build a full restaurant system unless explicitly requested.

---

## 6.9 Apartment

Apartment is part of the north-east residential / leisure zone.

It currently acts mainly as:

- district identity
- density
- residential landmark

It does not require full gameplay interior in this pass.

---

## 6.10 Workplace

Workplace remains the major work-life anchor.

Requirements:

- located beside Convenience Store
- relatively farther from Villa than home-area shops
- retains existing playable interior
- retains current work systems

Entrance logic:

- Front Entrance = north side, facing Main Street
- Back Entrance = south side, facing Delivery Alley / Smoking Area side

Do not reverse these.

The Back Entrance must remain available.

---

# 7. Entrance Icon Rules

Entrance symbols in the PNG are intentional layout data.

Do not ignore them.

## Front Entrance

Shown as:

- white arrow
- white doorway / box symbol

Meaning:

- public main entrance
- normal player-facing entrance
- generally placed toward Main Street or a public pedestrian street

## Back Entrance

Shown as:

- white arrow
- blue doorway / box symbol

Meaning:

- rear entrance
- service entrance
- alternate employee / story access

Currently most important for:

- Workplace

Entrance placement affects:

- player route
- Mike routes
- Story Zones
- navigation readability
- store flow
- future NPC behavior

---

# 8. Road / Walkable Surface Legend

## Gray

Gray represents:

- Road
- vehicle street
- Back Alley
- Delivery Alley
- Side Street
- other drivable / road-like surfaces

## Beige

Beige represents:

- sidewalk
- paved pedestrian area
- store frontage
- walkable building perimeter
- pedestrian transition space

Do not reinterpret beige walkable areas as grass.

---

# 9. Main Street

Main Street is the main east-west urban spine.

It should:

- remain wider than secondary streets
- visually connect major districts
- support future moving vehicles
- contain major pedestrian crossings
- contain traffic-light-controlled crossings
- remain easy for the player to understand spatially

Main Street is not just decoration.

It is the primary orientation axis of the district.

---

# 10. Crosswalk Rules

The reference now explicitly distinguishes important crosswalks.

A Crosswalk is shown using:

- white striped road markings

The Legend includes a Crosswalk icon.

Crosswalks should be preserved as explicit pedestrian crossing points.

Do not remove them during road reconstruction.

---

## 10.1 Major Main Street Crosswalks

Crosswalks that cross or directly connect to Main Street are considered major crossings.

These crossings should include Traffic Lights.

Each important Main Street crossing should have:

- one Traffic Light on one sidewalk side
- one Traffic Light on the opposite sidewalk side

In other words:

Major Crosswalk
= crosswalk + Traffic Light pair

The Traffic Lights should be positioned near the pedestrian crossing edges.

---

## 10.2 Minor Crosswalks

Small crosswalks on secondary / narrow streets do not automatically require Traffic Lights.

Examples:

- narrow local roads
- alley crossings
- small internal service road crossings

These can remain simple painted crossings unless explicitly upgraded later.

---

# 11. Traffic Light Rules

The reference now includes explicit Traffic Light icons.

Traffic Light icon:

- vertical signal
- red / yellow / green lights

The Legend includes:

`Traffic Light`

Traffic Lights should currently be treated as world-layout / blockout objects.

The full traffic system does NOT need to be implemented in this rebuild pass unless specifically requested.

However, their positions should be planned now so future systems can use them.

Future systems may include:

- moving cars
- vehicle stop / go phases
- pedestrian crossing phases
- traffic synchronization
- player crossing safety
- ambient city behavior

Therefore, Traffic Light placement should be consistent and reusable.

---

# 12. Traffic Light Placement

Major Main Street crossings in the final reference include Traffic Light pairs.

For each major crossing:

- place a Traffic Light near both pedestrian ends of the crossing
- orient them so they logically serve the street intersection
- keep them off the center walking path
- do not block pedestrian movement

The final reference contains multiple Main Street signalized crossing points.

Do not accidentally omit the central / eastern Main Street crossing.

The latest PNG is the authoritative source for exact crossing locations.

---

# 13. Future Traffic Compatibility

The road layout should remain compatible with future vehicle traffic.

During this blockout pass:

Do not implement full car traffic yet.

But avoid layouts where:

- roads suddenly become too narrow
- Traffic Lights are placed inside vehicle lanes
- crosswalks do not connect sidewalk-to-sidewalk
- intersections have impossible turning geometry
- building entrances block major crossings

Think of the road system as future traffic-ready blockout.

---

# 14. Park

Park is a public leisure area.

It should support:

- trees
- benches
- trash bins
- streetlamps
- walkable public-space feeling

The Park does not need complex gameplay yet.

---

# 15. Playground

Playground is separate from the Park.

It should remain:

- visually distinct
- part of the north-east residential leisure zone
- surrounded by reasonable pedestrian access

Detailed playground assets can come later.

---

# 16. Small Open Lot

Small Open Lot is an informal urban open space.

It is NOT:

- a large park
- a major gameplay building
- a required shortcut

It can later contain:

- sparse landscaping
- bench
- tree
- trash bin
- utility prop

but should remain mostly open.

---

# 17. Parking Area

Parking is located in the south-west.

Purpose:

- town-density variation
- outskirts-town atmosphere
- open paved space
- future car placement
- visual separation between blocks

It does not require active parking gameplay now.

---

# 18. Smoking Area

Smoking Area remains near Workplace.

It should remain associated with:

- Workplace Back Entrance
- Delivery Alley
- post-work route

Do not move it away from Workplace.

---

# 19. Alley / Service Road Logic

## Alley / Back Lane

Used behind residential / commercial blocks.

Purpose:

- urban depth
- service access feeling
- separation between front-facing and rear-facing space

## Back Alley

Used near Cafe / Pub / Fast Food commercial district.

## Delivery Alley

Used behind Workplace / Convenience Store.

Purpose:

- Workplace rear access
- Smoking Area connection
- service-zone identity

Do not reinterpret these automatically as special horror shortcuts.

---

# 20. No Forced Chase / Shortcut Interpretation

Important:

This map is currently a town-layout reference.

Do not automatically create:

- chase loops
- hidden escape routes
- shortcut mechanics
- Creature routing systems
- horror-specific path logic

unless explicitly requested later.

The road / alley network may support future horror gameplay naturally, but that is not the current implementation target.

---

# 21. Street Furniture Legend

Final map legend:

- small black square = Streetlamp
- small black circle = Trash Bin
- horizontal black rectangle = Bench
- black triangle = Tree
- vertical outlined rectangle = Vending Machine
- white arrow + white box = Front Entrance
- white arrow + blue box = Back Entrance
- red/yellow/green vertical icon = Traffic Light
- striped white road marking = Crosswalk

Use this legend consistently.

---

# 22. Streetlamp Placement Intent

Streetlamps should primarily support:

- Main Street
- major crossings
- commercial entrances
- Workplace area
- Park / Playground
- major secondary-street corners
- nighttime readability

They do not need centimeter-perfect placement from the image.

Preserve the general distribution logic.

---

# 23. Trash Bin Placement Intent

Trash bins fit naturally near:

- stores
- park
- playground
- commercial frontage
- back alley
- service / delivery zones
- workplace rear area

Do not spam them uniformly.

---

# 24. Bench Placement Intent

Benches should mainly appear in:

- Park
- Playground
- selected Main Street frontage
- public resting spots
- commercial / cafe-adjacent pedestrian space

---

# 25. Tree Placement Intent

Trees should mainly support:

- Park
- Playground
- residential sidewalks
- district edges
- selected Main Street sections
- visual separation between building masses

They should not block:

- important entrances
- crosswalk visibility
- Traffic Lights
- major story sightlines

---

# 26. Vending Machine Placement Intent

Vending machines should remain sparse.

Good areas:

- residential zone
- Game CD Shop vicinity
- Main Street commercial area
- workplace / service zone

Do not place one beside every building.

---

# 27. Existing Day 1 Story Integration

The current Day 1 Story Loop is already playable.

The town rebuild must reconnect it to the new layout.

Current major Day 1 world flow:

Villa
→ Mike meeting point
→ Workplace
→ Convenience Store
→ Workplace
→ Smoking Area
→ Villa

After rebuilding the town, update:

- Mike initial position
- commute route
- lunch route
- Convenience Store route
- return-to-work route
- Smoking Area route
- Mike exit route
- Story Zones
- Store Zones
- Workplace Zones
- Home Zone
- entrance references

Do not leave old world-space positions behind.

---

# 28. Existing General Mart Conversion Rule

The currently implemented interactive `General Mart` is the Day 1 lunch store.

That existing interactive building should be converted into:

`Convenience Store`

and moved to the Convenience Store position beside Workplace.

Update all relevant code / references accordingly.

Examples:

- object name
- display label
- store identity
- storeId
- Day 1 story checks
- Checkout references
- StoreExitGate references
- Mike Beer Corner route
- Mart story zones
- Lunch objective logic

Do not create two active stores that both still identify as `GeneralMart`.

---

# 29. New General Mart Rule

The new General Mart shown near Small Open Lot is a separate building.

Purpose:

- household goods
- tableware
- daily-use products
- general merchandise

For the current District Blockout pass:

- create its building blockout
- create correct entrance placement
- place it correctly in the district

Do not implement its full inventory / checkout / interior systems yet unless specifically requested.

---

# 30. Pub / Bar Rule

Pub / Bar should be created as a town building blockout.

Current requirements:

- visible from Main Street
- Front Entrance on Main Street side
- optional content building

Full pub gameplay is not required yet.

---

# 31. Fast Food Store Rule

Fast Food Store should be created as a commercial building blockout.

For now:

- exterior / blockout presence
- proper front entrance
- district placement

Full restaurant gameplay is not required.

---

# 32. Builder / Coordinate Safety

Existing Story Builders may contain hardcoded default positions.

Important:

After moving the town, inspect:

- Day1StoryBuilder
- Workplace lighting builder
- StoryNpc route builders
- any editor utility using fixed coordinates

If these builders recreate objects at old positions, update their defaults to match the new district layout.

Do not leave a situation where rebuilding Story objects restores the old town coordinates.

---

# 33. Root-Movement Safety

When moving major interactive buildings, prefer moving them as coherent root structures.

Before moving:

- Villa
- Workplace
- Convenience Store
- Grocery Store
- Cafe
- Game CD Shop

inspect whether child objects use:

- local coordinates
- world-space references
- external scene references
- triggers outside the root
- independent lighting
- spawned waypoint assumptions

Preserve working interiors and functionality.

Avoid rebuilding stable interiors from scratch.

---

# 34. Final Blockout Priorities

The intended order is:

1. Read final PNG + NOTES
2. Inspect current Scene
3. Establish road / sidewalk skeleton
4. Establish Main Street crossings
5. Place Traffic Lights at major Main Street crossings
6. Place Villa / home district
7. Place Game CD Shop
8. Place Grocery Store
9. Place General Mart blockout
10. Place Cafe
11. Place Pub / Bar
12. Place Fast Food Store
13. Place Convenience Store
14. Place Workplace
15. Place Park / Playground / Apartment
16. Place Facade buildings
17. Place Parking / Alley / Delivery Alley
18. Place street furniture
19. Rebuild Mike routes
20. Rebuild Story Zones
21. Reconnect Day 1
22. Run full regression test

---

# 35. Final Interpretation Rule

If the current Unity scene and PNG cannot match perfectly:

Prioritize:

1. building function
2. district relationship
3. entrance orientation
4. Main Street relationship
5. crosswalk / Traffic Light logic
6. Day 1 route continuity
7. exact position / scale

Do not sacrifice functional story logic just to match pixels.

But do not freely redesign the reference either.

---

# 36. Final Intent Summary

The town should read as:

- north = residential / neighborhood
- center = Main Street
- south-west = mixed commercial / optional town life
- north-east = civic / leisure
- south-east = workplace / service zone

The final blockout should feel denser, more believable, and more city-like than the previous prototype while preserving the existing playable Daily-Life systems.

Traffic Lights and Crosswalks are now part of the town planning reference because the roads are expected to support future vehicle traffic.