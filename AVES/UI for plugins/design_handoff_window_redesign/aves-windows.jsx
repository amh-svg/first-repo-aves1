/* ============================================================
   aves-windows.jsx — shared chrome + small windows
   Exports: AvesMark, TitleBar, Footer, Btn, Pill,
            SettingsList, SettingsInline, MultiPicker, Progress
   Driven by a `v` prop: "native" | "pastel" | "studio".
   ============================================================ */

const PARAMS = [
  "PMO_Description","Item_Number","Manufacturer","Hilti_Anchor_Type",
  "Load_Capacity_kN","Material_Grade","Fire_Rating","Weight_kg",
  "Cost_Code","Supplier_Ref","Finish","Corrosion_Class"
];
const SELECTED = ["PMO_Description","Manufacturer","Load_Capacity_kN","Fire_Rating"];

/* ---- tiny inline icons ---- */
const Chev = () => (
  <svg className="chev" viewBox="0 0 16 16" width="14" height="14" fill="none" stroke="currentColor" strokeWidth="1.6">
    <path d="M4 6l4 4 4-4" strokeLinecap="round" strokeLinejoin="round"/>
  </svg>
);
const Arrow = () => (
  <svg className="row-arrow" viewBox="0 0 16 16" width="15" height="15" fill="none" stroke="currentColor" strokeWidth="1.6">
    <path d="M6 4l4 4-4 4" strokeLinecap="round" strokeLinejoin="round"/>
  </svg>
);
const Check = () => (
  <svg viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="2.2">
    <path d="M3.5 8.5l3 3 6-7" strokeLinecap="round" strokeLinejoin="round"/>
  </svg>
);
const SearchIcon = () => (
  <svg viewBox="0 0 16 16" width="15" height="15" fill="none" stroke="currentColor" strokeWidth="1.6">
    <circle cx="7" cy="7" r="4.2"/><path d="M10.5 10.5L14 14" strokeLinecap="round"/>
  </svg>
);

/* ---- brand mark ----
   Neutral placeholder tile (NOT the company's real logo). Picks up the
   accent colour so palette swaps show here too. Swap for the real mark later. */
const AvesMark = ({ v, size = 20 }) => {
  if (v === "studio") return <div className="studio-dot" style={{ width: size, height: size }} />;
  if (window.AVES_HIDE_MARK) return null;
  return (
    <span className="tb-mark aves-tile" style={{ width: size, height: size }}>
      <span className="aves-tile-dot" />
    </span>
  );
};

/* ---- window controls ---- */
const WinCtl = () => (
  <div className="tb-ctl">
    <svg viewBox="0 0 12 12"><path d="M2 6h8" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round"/></svg>
    <svg viewBox="0 0 12 12"><rect x="2" y="2" width="8" height="8" rx="1.5" stroke="currentColor" strokeWidth="1.3" fill="none"/></svg>
    <svg viewBox="0 0 12 12"><path d="M3 3l6 6M9 3l-6 6" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round"/></svg>
  </div>
);

const TitleBar = ({ v, title, sub }) => (
  <div className="tb">
    <AvesMark v={v} />
    <span className="tb-title">{title}</span>
    {sub && <span className="tb-sub">· {sub}</span>}
    <span className="tb-spacer" />
    <WinCtl />
  </div>
);

const Footer = ({ children }) => <div className="win-foot">{children}</div>;
const Btn = ({ kind, children }) => <button className={"btn" + (kind ? " btn-" + kind : "")}>{children}</button>;

const PILL = {
  ok:   { c: "pill-ok",   t: "Done" },
  skip: { c: "pill-skip", t: "Skipped" },
  fail: { c: "pill-fail", t: "Failed" },
  warn: { c: "pill-warn", t: "Cache mismatch" },
  info: { c: "pill-info", t: "Info" },
};
const Pill = ({ kind, children }) => {
  const p = PILL[kind] || PILL.info;
  return <span className={"pill " + p.c}><span className="dot" />{children || p.t}</span>;
};

/* ---- Studio sidebar shell ---- */
const StudioShell = ({ steps, active, title, sub, children, foot }) => (
  <div className="studio-shell">
    <div className="studio-side">
      <div className="brand">
        <AvesMark v="studio" size={26} />
        <div><b>AVES</b><small>FAMILY TOOLS</small></div>
      </div>
      <div className="studio-steps">
        {steps.map((s, i) => (
          <div key={i} className={"studio-step " + (i < active ? "done" : i === active ? "on" : "")}>
            <span className="sn">{i < active ? "✓" : i + 1}</span>{s}
          </div>
        ))}
      </div>
      <div className="foot">AVES v25.03 · WIP<br/>Revit 2025</div>
    </div>
    <div className="studio-main">
      <div className="studio-head"><h2>{title}</h2><p>{sub}</p></div>
      <div className="win-body" style={{ flex: "1 1 auto" }}>{children}</div>
      {foot}
    </div>
  </div>
);

/* =====================================================================
   SETTINGS — styled list (original double-click pattern, refined)
   ===================================================================== */
const SETTINGS_ROWS = [
  { k: "Group", v: "MEKANOS" },
  { k: "Parameters", v: "4 selected", sel: true },
  { k: "Revit Group", v: "Identity Data" },
  { k: "Mode / Category", v: "Use Current Selection" },
  { k: "Parameter Scope", v: "Instance" },
];

const ListBody = ({ v }) => (
  <>
    <div className="list">
      {SETTINGS_ROWS.map((r, i) => (
        <div key={i} className={"row" + (r.sel ? " sel" : "")}>
          <span className="field-num">{i + 1}</span>
          <span className="row-key">{r.k}</span>
          <span className="row-val">{r.v}</span>
          <Arrow />
        </div>
      ))}
    </div>
    <div className="hint">Double-click a row to edit it, then run the script.</div>
  </>
);

const SettingsList = ({ v }) => {
  const foot = (
    <Footer><span className="foot-spacer" /><Btn kind="ghost">Cancel</Btn><Btn kind="primary">▶ Run script</Btn></Footer>
  );
  if (v === "studio")
    return (
      <div className="win">
        <StudioShell steps={["Configure", "Select families", "Review report"]} active={0}
          title="Batch Shared Parameters" sub="Configure the injection, then run." foot={foot}>
          <ListBody v={v} />
        </StudioShell>
      </div>
    );
  return (
    <div className="win">
      <TitleBar v={v} title="Batch Shared Parameters" sub="Settings" />
      {v === "pastel" && <div className="pastel-band"><h2>Configure injection</h2><p>Five settings — review each before running.</p></div>}
      <div className="win-body"><ListBody v={v} /></div>
      {foot}
    </div>
  );
};

/* =====================================================================
   SETTINGS — inline controls (no sub-dialogs)
   ===================================================================== */
const InlineBody = ({ v }) => (
  <>
    <div className="field">
      <div className="field-label"><span className="field-num">1</span>Shared parameter group</div>
      <div className="control">MEKANOS <Chev /></div>
    </div>
    <div className="field">
      <div className="field-label"><span className="field-num">2</span>Parameters to inject</div>
      <div className="chips">
        {SELECTED.map(p => (
          <span key={p} className="chip"><span className="chip-mono">{p}</span><span className="x">×</span></span>
        ))}
        <span className="chip" style={{ background: "transparent", color: "var(--text-faint)", border: "1px dashed var(--border-strong)" }}>+ Add</span>
      </div>
    </div>
    <div className="field" style={{ display: "flex", gap: 14 }}>
      <div style={{ flex: 1 }}>
        <div className="field-label"><span className="field-num">3</span>Target Revit group</div>
        <div className="control">Identity Data <Chev /></div>
      </div>
      <div style={{ flex: 1 }}>
        <div className="field-label"><span className="field-num">4</span>Mode / category</div>
        <div className="control">Use Current Selection <Chev /></div>
      </div>
    </div>
    <div className="field" style={{ marginBottom: 0 }}>
      <div className="field-label"><span className="field-num">5</span>Parameter scope</div>
      <div className="seg"><button className="on">Instance</button><button>Type</button></div>
    </div>
  </>
);

const SettingsInline = ({ v }) => {
  const foot = (
    <Footer><span className="foot-spacer" /><Btn kind="ghost">Cancel</Btn><Btn kind="primary">▶ Run script</Btn></Footer>
  );
  if (v === "studio")
    return (
      <div className="win">
        <StudioShell steps={["Configure", "Select families", "Review report"]} active={0}
          title="Batch Shared Parameters" sub="All options inline — no pop-ups." foot={foot}>
          <InlineBody v={v} />
        </StudioShell>
      </div>
    );
  return (
    <div className="win">
      <TitleBar v={v} title="Batch Shared Parameters" sub="Settings" />
      {v === "pastel" && <div className="pastel-band"><h2>Configure injection</h2><p>Set everything inline, then run.</p></div>}
      <div className="win-body"><InlineBody v={v} /></div>
      {foot}
    </div>
  );
};

/* =====================================================================
   MULTI-PICKER
   ===================================================================== */
const MultiPicker = ({ v }) => (
  <div className="win">
    <TitleBar v={v} title="Select parameters to inject" />
    <div className="win-body" style={{ display: "flex", flexDirection: "column" }}>
      <div className="search"><SearchIcon /> Filter parameters…</div>
      <div className="list" style={{ flex: "1 1 auto" }}>
        {PARAMS.slice(0, 8).map(p => {
          const on = SELECTED.includes(p);
          return (
            <div key={p} className="cbrow">
              <span className={"cb" + (on ? " on" : "")}>{on && <Check />}</span>
              <span className="nm mono">{p}</span>
            </div>
          );
        })}
      </div>
    </div>
    <Footer>
      <span style={{ fontSize: 12, color: "var(--text-dim)", fontWeight: 600 }}>{SELECTED.length} selected</span>
      <span className="foot-spacer" /><Btn kind="ghost">Cancel</Btn><Btn kind="primary">Add parameters</Btn>
    </Footer>
  </div>
);

/* =====================================================================
   PROGRESS
   ===================================================================== */
const Progress = ({ v }) => (
  <div className="win">
    <TitleBar v={v} title="Processing families" />
    <div className="win-body" style={{ display: "flex", flexDirection: "column", gap: 14, justifyContent: "center" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "baseline" }}>
        <span style={{ fontSize: 13, fontWeight: 600 }}>Processing: <span style={{ fontFamily: "var(--font-mono)", fontSize: 12 }}>FTE50 TE200/200 FRP</span></span>
        <span style={{ fontFamily: "var(--font-mono)", fontSize: 12, color: "var(--text-dim)" }}>12 / 20</span>
      </div>
      <div className="pbar"><i style={{ width: "60%" }} /></div>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
        <span style={{ fontSize: 11.5, color: "var(--text-faint)" }}>Adding 4 parameters · 8 families remaining</span>
        <button className="btn btn-ghost" style={{ padding: "6px 14px" }}>Cancel</button>
      </div>
    </div>
  </div>
);

Object.assign(window, {
  AvesMark, TitleBar, Footer, Btn, Pill, StudioShell,
  SettingsList, SettingsInline, MultiPicker, Progress,
  PARAMS, SELECTED,
});
