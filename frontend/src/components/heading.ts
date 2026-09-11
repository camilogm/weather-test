/** Which way the journey went, and so which edge the new place arrives from. */
export type Heading = 'east' | 'west' | 'none'

/**
 * The compass heading of a move from one longitude to another, along the
 * shortest arc.
 *
 * Plain subtraction is wrong at the antimeridian, and wrong in the loudest
 * possible way: Tokyo to Los Angeles is a hundred degrees east across the
 * Pacific, but `to - from` reports 257 degrees WEST and would send the page
 * sliding back across every meridian the traveller did not cross. Folding the
 * difference into (-180, 180] picks the arc a person would actually describe.
 *
 * Exactly half a world apart there is no shorter arc, so the answer is a tie.
 * It resolves west, and it resolves west every single time — a stable arbitrary
 * answer reads as a decision, while one that alternated would read as a bug.
 */
export function headingBetween(fromLongitude: number, toLongitude: number): Heading {
  // +540 rather than +180 keeps the operand positive before the remainder, so
  // this does not depend on how the language signs a negative modulo.
  const degrees = ((toLongitude - fromLongitude + 540) % 360) - 180

  if (degrees === 0) {
    return 'none'
  }

  return degrees > 0 ? 'east' : 'west'
}
