/* ============================================================
   aves-report.jsx — Unified Report window (the hero)
   Exports: Report.  Prop: v = "native" | "pastel" | "studio"
   ============================================================ */

const R_ROWS = [
  { fam: "FTE50 TE200/200 FRP", st: "ok",   added: "4", existed: "0", params: "PMO_Description, Manufacturer, Load_Capacity_kN, Fire_Rating", note: "" },
  { fam: "Bracket_Heavy_M12",   st: "ok",   added: "4", existed: "1", params: "PMO_Description, Manufacturer, Fire_Rating, Cost_Code", note: "" },
  { fam: "PipeHanger_HD200",    st: "warn", added: "4", existed: "0", params: "PMO_Description, Manufacturer, Load_Capacity_kN, Fire_Rating", note: "Cache forced group: Load_Capacity_kN → Dimensions" },
  { fam: "DuctElbow_Galv_90",   st: "ok",   added: "4", existed: "2", params: "PMO_Description, Manufacturer, Fire_Rating, Weight_kg", note: "" },
  { fam: "CableTray_Support_U", st: "skip", added: "0", existed: "—", params: "—", note: "System / Non-Editable" },
  { fam: "Anchor_Legacy_X",     st: "fail", added: "—", existed: "—", params: "—", note: "Cannot open for editing" },
];

const STATS = [
  { k: "Processed", v: "16", cls: "ok" },
  { k: "Skipped",   v: "2",  cls: "" },
  { k: "Failed",    v: "1",  cls: "fail" },
  { k: "Params added", v: "64", cls: "accent" },
  { k: "Already existed", v: "8", cls: "" },
  { k: "Cache mismatch", v: "1", cls: "warn" },
];

const KV = [
  { k: "Parameter group", v: "MEKANOS" },
  { k: "Mode", v: "Use Selection" },
  { k: "Scope", v: "Instance" },
  { k: "Families found", v: "20" },
];

const cell = (txt) => txt === "—"
  ? <span className="dash">—</span>
  : <span className="num">{txt}</span>;

const ReportInner = ({ v }) => (
  <>
    {/* summary zone */}
    <div style={{ display: "flex", gap: 22, marginBottom: 22 }}>
      <div style={{ flex: "1 1 auto" }}>
        <div className="sec-label">Run summary</div>
        <div className="stats" style={{ gridTemplateColumns: "repeat(3, 1fr)" }}>
          {STATS.map(s => (
            <div key={s.k} className={"stat " + s.cls}>
              <span className="v">{s.v}</span><span className="k">{s.k}</span>
            </div>
          ))}
        </div>
      </div>
      <div style={{ flex: "0 0 250px" }}>
        <div className="sec-label">Configuration</div>
        <div className="kv" style={{ background: "var(--surface-2)", border: "1px solid var(--border)", borderRadius: "var(--radius-sm)", padding: "4px 14px" }}>
          {KV.map(r => (
            <div key={r.k} className="kv-row"><span className="k">{r.k}</span><span className="v mono">{r.v}</span></div>
          ))}
        </div>
      </div>
    </div>

    {/* results table */}
    <div className="sec-label" style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
      <span>Results per family</span>
      <span style={{ color: "var(--text-faint)", fontWeight: 600, textTransform: "none", letterSpacing: 0 }}>showing 6 of 20</span>
    </div>
    <div style={{ border: "1px solid var(--border)", borderRadius: "var(--radius-sm)", overflow: "hidden" }}>
      <table className="tbl">
        <thead>
          <tr>
            <th>Family name</th><th>Status</th><th style={{ textAlign: "center" }}>Added</th>
            <th style={{ textAlign: "center" }}>Existed</th><th>Added parameters</th><th>Notes</th>
          </tr>
        </thead>
        <tbody>
          {R_ROWS.map((r, i) => (
            <tr key={i}>
              <td className="fam">{r.fam}</td>
              <td><Pill kind={r.st} /></td>
              <td className="num">{cell(r.added)}</td>
              <td className="num">{cell(r.existed)}</td>
              <td className={"params" + (r.params !== "—" ? " mono" : "")}>{r.params === "—" ? <span className="dash">—</span> : r.params}</td>
              <td className={"note" + (r.st === "warn" ? " warn" : "")}>{r.note || <span className="dash">—</span>}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  </>
);

const ReportFoot = () => (
  <Footer>
    <span style={{ fontSize: 11.5, color: "var(--text-faint)" }}>Source · MEKANOS.txt</span>
    <span className="foot-spacer" />
    <Btn kind="">⧉ Copy to clipboard</Btn>
    <Btn kind="primary">Close</Btn>
  </Footer>
);

const Report = ({ v }) => {
  if (v === "studio")
    return (
      <div className="win">
        <StudioShell steps={["Configure", "Select families", "Review report"]} active={2}
          title="Batch parameter injection" sub="16 of 20 families processed · 64 parameters added" foot={<ReportFoot />}>
          <ReportInner v={v} />
        </StudioShell>
      </div>
    );
  return (
    <div className="win">
      <TitleBar v={v} title="Batch parameter injection" sub="Results" />
      {v === "pastel" && <div className="pastel-band"><h2>Injection complete</h2><p>16 of 20 families processed · 64 parameters added.</p></div>}
      <div className="win-body" style={{ overflow: "hidden" }}><ReportInner v={v} /></div>
      <ReportFoot />
    </div>
  );
};

Object.assign(window, { Report });
