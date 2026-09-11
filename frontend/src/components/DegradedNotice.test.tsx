import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import type { Provenance } from '../api/types'
import { DegradedNotice } from './DegradedNotice'

function provenance(overrides: Partial<Provenance> = {}): Provenance {
  return {
    source: 'StaleCache',
    degraded: true,
    retrievedAt: '2026-09-07T12:00:00+00:00',
    ...overrides,
  }
}

const notice = () => screen.getByRole('status').textContent

describe('DegradedNotice', () => {
  it('says nothing when the answer came from a live call', () => {
    const { container } = render(
      <DegradedNotice provenance={provenance({ source: 'Provider', degraded: false })} />,
    )

    expect(container.innerHTML).toBe('')
  })

  /*
    The role is the contract, not the tag. This is what lets the element carrying
    it change without the announcement changing: a screen reader reaches this
    notice because it is a status region, and it must keep doing so.
  */
  it('announces itself as a status region so a screen reader reaches it', () => {
    render(<DegradedNotice provenance={provenance()} />)

    expect(notice()).toContain('Mostrando datos anteriores.')
  })

  it('blames the cache when the data came from a stale one', () => {
    render(<DegradedNotice provenance={provenance({ source: 'StaleCache' })} />)

    expect(notice()).toContain('el último dato que quedó en caché')
  })

  it('blames the stored snapshot when the data came from history', () => {
    render(<DegradedNotice provenance={provenance({ source: 'Historical' })} />)

    expect(notice()).toContain('el último pronóstico que quedó guardado')
  })

  it('dates the data it is showing', () => {
    render(<DegradedNotice provenance={provenance()} />)

    expect(notice()).toContain('Última actualización:')
  })
})
