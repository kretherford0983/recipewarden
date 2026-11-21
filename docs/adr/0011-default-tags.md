# ADR 0011: Built-in default tags

- Status: Accepted (2026-11-21, owner)
- Requirements: FR-ORG-01, FR-HOME-02 (season tags), AC-20

## Context
FR-ORG-01 makes tags free-form. On the first deployed build the owner asked for a starting set, so common tags are
spelled one way from the start, and a tag for recipes made to be used inside other recipes (homemade onion soup
mix, pizza dough, stock).

## Decision
1. Every install starts with these typed tags (`DefaultTags` in Core):
   - Meal: Breakfast, Brunch, Lunch, Dinner, Snack
   - Course: Appetizer, Entree, Side Dish, Soup, Salad, Dessert, Bread, Sauce, Drink, Homemade Ingredient
   - Season: Spring, Summer, Fall, Winter, Holiday (used by seasonal recommendations, FR-HOME-02)
   - Method: Slow Cooker, Pressure Cooker, Air Fryer, Grill, Sheet Pan, One Pot, No Cook
   - Diet: Vegetarian, Vegan, Gluten-Free, Dairy-Free
   - Cuisine: none; left to the household.
2. They are ordinary tags: editors can rename, retype or delete them, and create their own.
3. They are seeded **once**, by the migration `M1_RangesAndDefaultTags`, with insert-if-missing SQL: a tag with the
   same normalized name or slug that already exists is kept, and only gets the type if it has none. A default an
   editor deletes stays deleted (nothing re-adds it at startup).
4. Readers' tag lists show only tags in use; editors see all tags in the tag field's suggestions and on
   /manage/tags.

## Consequences
- Later changes to the default list need a new migration; the M1 migration's SQL is fixed text.
- Linking a "Homemade Ingredient" recipe into other recipes' ingredient lists is not part of this; it would need
  its own requirement.
