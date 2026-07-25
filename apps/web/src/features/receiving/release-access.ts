export function canApproveReceivingRelease(profile: Record<string, unknown> | undefined): boolean {
  const claim = profile?.capability
  if (typeof claim === 'string') return claim === 'receiving.release.approve'
  if (Array.isArray(claim)) return claim.some(value => value === 'receiving.release.approve')
  return false
}
