import { Alert, Modal, Select, Typography } from 'antd'
import { useMemo, useState } from 'react'
import type { CombatConfiguration } from '../../contracts/bridge'
import { supportedLogicalKeys } from './combatConfiguration'

const { Text } = Typography
type KeyConfiguration = Pick<CombatConfiguration, 'singleAttackKey' | 'areaAttackKey' | 'hpPotionKey' | 'mpPotionKey' | 'jumpKey' | 'pickupKey'>

const fields: { key: keyof KeyConfiguration; label: string; hint: string }[] = [
  { key: 'singleAttackKey', label: '单体攻击', hint: '默认 Ctrl' },
  { key: 'areaAttackKey', label: '群体攻击', hint: '可与单体攻击共用 Ctrl' },
  { key: 'hpPotionKey', label: 'HP 药水', hint: '默认 Delete' },
  { key: 'mpPotionKey', label: 'MP 药水', hint: '默认 End' },
  { key: 'jumpKey', label: '跳跃', hint: '默认 Alt' },
  { key: 'pickupKey', label: '拾取', hint: '默认 Z' },
]

export function KeyBindingEditor({ configuration, onCancel, onSave }: { configuration: CombatConfiguration; onCancel(): void; onSave(value: KeyConfiguration): void }) {
  const [draft, setDraft] = useState<KeyConfiguration>(() => configuration)
  const conflict = useMemo(() => {
    const attackKeys = [draft.singleAttackKey, draft.areaAttackKey].map((value) => value.toLocaleUpperCase())
    const exclusiveKeys = [draft.hpPotionKey, draft.mpPotionKey, draft.jumpKey, draft.pickupKey].map((value) => value.toLocaleUpperCase())
    return new Set(exclusiveKeys).size !== exclusiveKeys.length
      || attackKeys.some((value) => exclusiveKeys.includes(value))
  }, [draft])

  return (
    <Modal title="按键配置" open onCancel={onCancel} onOk={() => onSave(draft)} okText="保存配置" cancelText="取消" okButtonProps={{ disabled: conflict }} destroyOnHidden>
      <div className="key-editor-grid">
        {fields.map((field) => <label className="key-editor-field" key={field.key}>
          <span><Text className="field-title">{field.label}</Text><Text className="field-hint">{field.hint}</Text></span>
          <Select aria-label={field.label} value={draft[field.key]} options={supportedLogicalKeys.map((value) => ({ value, label: value }))} onChange={(value) => setDraft((current) => ({ ...current, [field.key]: value }))} />
        </label>)}
      </div>
      {conflict && <Alert className="key-editor-alert" type="error" showIcon message="按键冲突" description="攻击、药水、跳跃和拾取不能使用同一个键。" />}
    </Modal>
  )
}
