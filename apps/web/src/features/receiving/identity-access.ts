function hasCapability(profile: Record<string, unknown> | undefined, capability: string): boolean {
  const claim = profile?.capability
  if (typeof claim === 'string') return claim === capability
  if (Array.isArray(claim)) return claim.some(value => value === capability)
  return false
}

export function hasIdentityEvaluateCapability(profile: Record<string, unknown> | undefined): boolean {
  return hasCapability(profile, 'receiving.identity.evaluate')
}
