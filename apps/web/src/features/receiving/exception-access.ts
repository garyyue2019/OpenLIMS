function hasCapability(profile: Record<string, unknown> | undefined, capability: string): boolean {
  const claim = profile?.capability
  if (typeof claim === 'string') return claim === capability
  if (Array.isArray(claim)) return claim.some(value => value === capability)
  return false
}

export const canCreateException = (profile: Record<string, unknown> | undefined): boolean =>
  hasCapability(profile, 'exception.create')
export const canReadException = (profile: Record<string, unknown> | undefined): boolean =>
  hasCapability(profile, 'exception.read')
export const canQualityApproveException = (profile: Record<string, unknown> | undefined): boolean =>
  hasCapability(profile, 'exception.quality.approve')
export const canEhsApproveException = (profile: Record<string, unknown> | undefined): boolean =>
  hasCapability(profile, 'exception.ehs.approve')
