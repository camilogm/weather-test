import { describe, expect, it } from 'vitest'

import { headingBetween } from './heading'

/** Longitudes of the places the cases below travel between. */
const BOGOTA = -74.08
const MADRID = -3.7
const TOKYO = 139.69
const LOS_ANGELES = -118.24
const SUVA = 178.44
const APIA = -171.76

describe('headingBetween', () => {
  it('reads a move to a greater longitude as travelling east', () => {
    expect(headingBetween(BOGOTA, MADRID)).toBe('east')
  })

  it('reads a move to a lesser longitude as travelling west', () => {
    expect(headingBetween(MADRID, BOGOTA)).toBe('west')
  })

  it('has no heading when the destination is the place already shown', () => {
    expect(headingBetween(BOGOTA, BOGOTA)).toBe('none')
  })

  /*
    The two cases the naive subtraction gets wrong. Tokyo to Los Angeles is 103
    degrees east across the Pacific, but `to - from` reports -257 and would send
    the page sliding the long way round the planet — past every city it did not
    go to. The shortest arc is the only one a person would describe as the
    direction they travelled.
  */
  it('crosses the antimeridian eastward rather than going the long way back', () => {
    expect(headingBetween(TOKYO, LOS_ANGELES)).toBe('east')
  })

  it('crosses the antimeridian westward rather than going the long way round', () => {
    expect(headingBetween(APIA, SUVA)).toBe('west')
  })

  it('keeps the shortest arc for a pair straddling the prime meridian', () => {
    expect(headingBetween(-1, 1)).toBe('east')
    expect(headingBetween(1, -1)).toBe('west')
  })

  /*
    Half a world away there is no shorter arc, so neither heading is more true
    than the other. What matters is that the answer is the SAME every time:
    an antipode that slid east on one visit and west on the next would read as
    a bug rather than as a coin toss.
  */
  it('answers antipodes consistently instead of flickering between headings', () => {
    expect(headingBetween(0, 180)).toBe(headingBetween(0, 180))
    expect(headingBetween(0, -180)).toBe(headingBetween(0, 180))
  })

  it('ignores latitude entirely — only the east–west axis moves the page', () => {
    // Same longitude, wildly different places. Nothing slides.
    expect(headingBetween(-74.08, -74.08)).toBe('none')
  })
})
