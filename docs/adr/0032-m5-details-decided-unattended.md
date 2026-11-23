# ADR 0032: M5 details decided while the owner was away (for review)

- Status: Proposed — **owner review requested** (2026-11-23). Built as described; each point is easy to change.
- Requirements: FR-SHOP-01 to 04, FR-EXP-01 to 03, FR-VIEW-02, BR-41, AC-23, AC-24, AC-25; ADR 0028 to 0031

## Context
The owner asked for M5 to be built without stopping for questions (2026-11-23) and for open points to be recorded
for review. ADRs 0027 to 0031 hold the decisions the owner made; this ADR holds the smaller ones made while building.

## Decisions
1. **Adding a recipe to a list** happens on the recipe page (Editors only), at the scale shown, into a chosen list or
   a new one named in the same form. A typed name always starts a new list; an empty one is called "Shopping".
   The post goes to `/shopping/new?handler=AddRecipe`, so all list creation sits behind one Editor-only page.
2. **Manual items** ("2 lemons") are read with the ingredient parser and matched to a food, so they merge with recipe
   lines and get a section and a package count like any other item.
3. **Food.PackageName** is a new column ("stick", "can", "carton"). PackageSize with PackageUnitId (a volume or
   weight unit, or none for a count) is the package; both are edited on `/manage/conversions`.
4. **Section walking order:** Produce, Bakery, Meat and Seafood, Dairy and Eggs, Deli, Baking, Pantry, Canned Goods,
   Pasta and Grains, Spices, Condiments, Snacks, Beverages, Frozen, then Other. Editors pick a food's section from
   this list. The seed maps USDA categories to these sections. Nuts go to Snacks and "Other" foods to Beverages,
   since those foods are coffee, juice and water.
5. **Limits:** list names 80 characters; items 200; 1,000 lines and 50 recipes per list.
6. **Tick keys** are checked against the list's current items, so an anonymous request cannot store arbitrary keys.
   An unknown key or list returns 404.
7. **Export of a scaled view** writes the scaled lines and the scaled servings. Steps are written as stored.
   Temperatures are not converted in export (they are on the page and in print).
8. **Bulk export layout:** one folder per recipe (`{slug}/{slug}.md`, `{slug}/images/…`), so relative image links
   match a single-recipe export. Recipes are exported at 1x. A collection's zip keeps the collection order.
9. **Print** has no images and no step photos, so a long recipe still fits on one page. It shows the source URL
   written out (AC-12), and ingredients in two columns. It has its own minimal layout and `print.css`.
10. **Cook mode timers** start at the low end of a range. Bare "h", "m" and "s" are ignored ("2 m strips"), and so
    is anything over 24 hours. A finished timer vibrates where supported. There is no sound, so nothing new is
    needed for autoplay or for permissions.
11. **The finished-list notice** shows the minutes left ("removed in 15 minutes") rather than a clock time. The
    server does not know the reader's time zone.

## Consequences
If the owner changes any of these, the change is local: one Core rule, page or seed value, each with tests.
