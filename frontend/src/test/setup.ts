import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

// Testing Library unmounts automatically only when globals are on. They are
// not, so the teardown is wired here instead — without it, every test leaves
// its component mounted and effects from one bleed into the next.
afterEach(cleanup)
