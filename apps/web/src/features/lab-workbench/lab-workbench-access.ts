export type LabClaims = Readonly<Record<string, unknown>> | undefined

export function hasLabCapability(claims: LabClaims, requiredCapability: string): boolean {
  const claim = claims?.capability
  if (typeof claim === 'string') return claim === requiredCapability
  return Array.isArray(claim) && claim.some(value => value === requiredCapability)
}
