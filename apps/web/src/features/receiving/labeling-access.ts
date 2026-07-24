function hasCapability(profile: Record<string, unknown> | undefined, capability: string): boolean {
  const claim = profile?.capability
  if (typeof claim === 'string') return claim === capability
  if (Array.isArray(claim)) return claim.some(value => value === capability)
  return false
}

export function hasLabelPrintCapability(profile: Record<string, unknown> | undefined): boolean {
  return hasCapability(profile, 'receiving.label.print')
}

export function hasLabelScanCapability(profile: Record<string, unknown> | undefined): boolean {
  return hasCapability(profile, 'receiving.label.scan')
}

export function hasLabelReprintCapability(profile: Record<string, unknown> | undefined): boolean {
  return hasCapability(profile, 'receiving.label.reprint')
}
