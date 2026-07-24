export function hasReceivingRegisterCapability(profile: Record<string, unknown> | undefined): boolean {
  const claim = profile?.capability
  if (typeof claim === 'string') return claim === 'receiving.register'
  if (Array.isArray(claim)) return claim.some(value => value === 'receiving.register')
  return false
}
