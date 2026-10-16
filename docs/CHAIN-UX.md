# Processing-chain interface — AH-314

The list is the execution order. The summary includes the pre-resize attached to each existing ONNX upscaler: placing RIFE above that upscaler means RIFE runs before the resize.

Only RIFE offers Quality/Fast. FRUC has one implemented SDR8 ×2 mode; an ONNX spatial stage has no temporal mode. Historical shared RIFE settings live in the historical chain editor, hidden for an ordered chain, and have been removed from Global Settings. Their configuration keys remain intact for existing profiles.

This UI lot does not change the video pipeline, model arithmetic, SDK provisioning or HDR placement. Independent downscale, archive import and explicit HDR stages are tracked in AH-315/316/317.

Qualification: actual Avalonia headless controls, stage-mode binding, order summary after reorder, export/reload and source-comment preservation. Eight relevant assertions pass; the initial export-comment assertion was corrected because a new export does not copy comments from another file. Only that failed case was rerun. A rendered frame was inspected. No video trial is necessary for this interface-only change.
