import es from './es.json'

/**
 * Every user-facing string in the application lives in `es.json` and nowhere
 * else. There is no language switcher — the product is Spanish — but the copy is
 * still kept out of the components, for two reasons that matter more than
 * switching languages ever did:
 *
 *  1. Someone who is not a developer can read and correct the whole product's
 *     wording in one file, without opening a single .tsx.
 *  2. A missing or misspelled key fails the BUILD rather than rendering blank
 *     space in front of a user, because `CopyKey` is derived from the JSON.
 *
 * Adding a second locale later means adding a second file and choosing between
 * them here. Nothing else in the codebase would change.
 */

/** Latin American Spanish, so dates and numbers do not follow the browser's guess. */
export const LOCALE = 'es-419'

type Copy = typeof es

/** Every dotted path in the dictionary that resolves to a string. */
type PathsOf<T> = {
  [K in keyof T & string]: T[K] extends string ? K : `${K}.${PathsOf<T[K]>}`
}[keyof T & string]

export type CopyKey = PathsOf<Copy>

/** Values substituted into `{placeholder}` slots. */
export type CopyValues = Record<string, string | number>

/**
 * Looks a key up. Returns null rather than throwing, so a lookup that depends on
 * runtime data — a weather condition the API added yesterday, say — can fall
 * back instead of taking the page down.
 */
function lookup(key: string): string | null {
  let node: unknown = es

  for (const segment of key.split('.')) {
    if (typeof node !== 'object' || node === null || !(segment in node)) {
      return null
    }

    node = (node as Record<string, unknown>)[segment]
  }

  return typeof node === 'string' ? node : null
}

function interpolate(template: string, values: CopyValues): string {
  return template.replace(/\{(\w+)\}/g, (whole, name: string) =>
    name in values ? String(values[name]) : whole,
  )
}

/** Translates a known key. The type parameter makes a typo a compile error. */
export function t(key: CopyKey, values?: CopyValues): string {
  const template = lookup(key) ?? key
  return values ? interpolate(template, values) : template
}

/**
 * Picks between a singular and a plural wording for a count.
 *
 * Intl decides which category the number falls into rather than a `count === 1`
 * in a component, for the same reason LOCALE is written out elsewhere: the rule
 * belongs to the language. Spanish only has the two, but a locale added later
 * may have more, and the call sites will not have to learn about it.
 */
export function plural(forms: { one: CopyKey; many: CopyKey }, count: number): string {
  const category = new Intl.PluralRules(LOCALE).select(count)
  return t(category === 'one' ? forms.one : forms.many, { count })
}

/**
 * Translates a value that only exists at runtime — a condition name straight off
 * the API. An unrecognised one falls back to what the server sent, which is
 * unpolished but readable, rather than to an empty string or a raw key.
 */
export function translateCondition(condition: string): string {
  return lookup(`conditions.${condition}`) ?? condition
}

/** Same idea for provenance labels. */
export function translateSource(source: string): string {
  return lookup(`sources.${source}`) ?? source
}
