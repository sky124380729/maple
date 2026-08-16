import { CONTRACT_SCHEMA_VERSION, type CombatConfiguration } from '../../contracts/bridge'

export const defaultCombatConfiguration: CombatConfiguration = {
  schemaVersion: CONTRACT_SCHEMA_VERSION,
  attackMode: 'single',
  hpThresholdMode: 'percent', hpThreshold: 50,
  mpThresholdMode: 'percent', mpThreshold: 30,
  singleAttackKey: 'Ctrl', areaAttackKey: 'Ctrl', hpPotionKey: 'Delete', mpPotionKey: 'End',
  jumpKey: 'Alt', pickupEnabled: true, pickupKey: 'Z',
  preferredDistancePx: 70, areaTargetCount: 3, switchCooldownMs: 1200,
}

export const supportedLogicalKeys = [
  'Alt', 'Ctrl', 'Shift', 'Space', 'Delete', 'End',
  '1', '2', '3', '4', '5', '6', '7', '8', '9', '0',
  ...'QWERTYUIOPASDFGHJKLZXCVBNM'.split(''),
] as const
