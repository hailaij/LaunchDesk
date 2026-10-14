# UI Usability Follow-Up / 界面交互优化

## Scope / 范围

本轮进一步将启动项的高级折叠分区替换为五个分类标签：基本信息、启动/停止、就绪检测、健康监控、日志/环境。固定底栏不变；数字验证自动切换分类，未保存的字段修改在关闭前确认。切换标签不会标记修改，恢复原字段值会取消修改状态。

The item editor now uses five tabs instead of the previous expandable sections. The fixed footer remains; validation navigates to the invalid field. Closing with edited values asks before discarding them. Navigation alone is not an edit.

主窗口保留添加、分组、搜索和批量启停；编辑、复制、删除、排序及导入/导出/恢复集中在“更多操作”。菜单按选择状态禁用不适用的操作。行按钮仍可键盘访问，选中、聚焦和悬停时增强显示；文件夹使用独立底色与成员数量。日志底层与现有图标字形不变。

Keep frequent actions visible and collect lower-frequency commands in More actions. Row buttons remain keyboard-accessible and become fully emphasized on selection, hover or keyboard focus. Folder groups have a distinct background and member count. Log internals and existing icon glyphs are unchanged.

- 清理警告显示在底部入口，点击查看去重后的详情，不弹出周期性提示。维护队列及日志清理警告仍按原策略保留。
- 手动列宽在当前会话的刷新、增删及尺寸/语言变化中保留。尚未增加跨重启列宽持久化；未手动修改的列随尺寸档位和语言调整。
- 数字字段拒绝非整数、溢出及超范围值，显示字段名、有效范围，并展开对应分区。隐藏的健康地址/重启参数保留原值。
- PID、运行时长、顺序与退出码右对齐。未提供的运行数值显示 `-`。管理器强制退出在界面与日志中显示 `0xE04C4401`，CLI 的原始整数契约不变。
- 保持原有 `2*:3*` 表格/日志比例及停止图标，不依据截图猜测缺字。

Cleanup warnings have a non-modal footer entry. Column resizing survives refresh and responsive/language changes within the session; persistence across restarts is not added. Numeric errors include field names and ranges. Numeric columns align right and forced exit formatting is shared, without changing CLI integers. The original splitter ratio and stop glyph are retained.

## Rendering Contract / 渲染契约

`LogProjection.Update` still returns `string[]`; `UpdateLines` additionally returns `ProjectedLogLine` with text, prefix length and channel. Existing export and disk records are unchanged. `LogView.SetLines` remains available; `SetProjectedLines` matches all metadata when retaining row identities.

通道颜色只作用于来源前缀：`stdout` 默认颜色、`stderr` 红色、`system` 黄色、`stdin` 绿色。正文中的 `ERROR` 不改变通道。链接使用独立颜色和 Ctrl+点击提示。主题更新不重建日志文档；选中行及高对比度使用对应系统/选中颜色。

Channel colors identify provenance only, not severity. Link navigation remains Ctrl+click. Theme changes update dynamic brushes without replacing documents; selection and high contrast override prefix/link colors appropriately.

## Verification / 验证

`tests/Verify-Ui.ps1` requires usability and modern-list evidence in both standard and compact-English smoke runs. Each run also opens Chinese and English item/settings dialogs at 620x500 and 480x400. Evidence covers column resizing, same-band language switching, cleanup warnings, numeric boundaries, fixed preflight placement, category bounds and navigation, unsaved-field detection, menu selection states, last-action bounds, typed-row identity, links and virtualization. Existing fatal-cleanup and log-performance gates remain mandatory.

Artifacts: `artifacts/verification/ui-review/` for the previous UI pass and `artifacts/verification/modern-ui/` for the current changes, including `*-usability.json`, `*-modern-list.json`, compact dialog screenshots, resource-key evidence and performance reports. Core regressions additionally check typed projection prefix/channel compatibility at truncation boundaries and forced-exit log formatting.

高对比度为资源策略模拟，不等于 Windows 高对比度实机验收。Windows 10 实机验收仍按用户约定暂缓。此前一小时耐久报告属于上一轮源码，不能作为本次界面变更后的耐久证据。

High contrast coverage simulates the production resource policy, not OS-level acceptance. Windows 10 real-machine acceptance remains deferred. The earlier one-hour soak is evidence for the preceding source state, not a new soak of these UI changes.
