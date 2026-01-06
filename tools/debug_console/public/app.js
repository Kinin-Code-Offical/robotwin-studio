const testList = document.getElementById("testList");
const testOutput = document.getElementById("testOutput");
const clearOutput = document.getElementById("clearOutput");
const downloadSnapshot = document.getElementById("downloadSnapshot");
const autoRefreshToggle = document.getElementById("autoRefreshToggle");
const refreshIntervalInput = document.getElementById("refreshInterval");
const logScanTail = document.getElementById("logScanTail");
const refreshAlerts = document.getElementById("refreshAlerts");
const lastUpdate = document.getElementById("lastUpdate");
const unityLatency = document.getElementById("unityLatency");
const logArea = document.getElementById("logArea");
const logTail = document.getElementById("logTail");
const logSearch = document.getElementById("logSearch");
const refreshLogs = document.getElementById("refreshLogs");
const logList = document.getElementById("logList");
const logOutput = document.getElementById("logOutput");
const refreshPorts = document.getElementById("refreshPorts");
const portList = document.getElementById("portList");
const unityConnected = document.getElementById("unityConnected");
const engineStatus = document.getElementById("engineStatus");
const sceneName = document.getElementById("sceneName");
const runMode = document.getElementById("runMode");
const simRunning = document.getElementById("simRunning");
const simTick = document.getElementById("simTick");
const simTime = document.getElementById("simTime");
const signalCount = document.getElementById("signalCount");
const validationCount = document.getElementById("validationCount");
const simAvgJitter = document.getElementById("simAvgJitter");
const simMaxJitter = document.getElementById("simMaxJitter");
const simAvgTick = document.getElementById("simAvgTick");
const simOverruns = document.getElementById("simOverruns");
const simFastPath = document.getElementById("simFastPath");
const simCorrective = document.getElementById("simCorrective");
const simBudgetOverruns = document.getElementById("simBudgetOverruns");
const telemetryFilter = document.getElementById("telemetryFilter");
const refreshTelemetry = document.getElementById("refreshTelemetry");
const telemetryList = document.getElementById("telemetryList");
const firmwarePerfList = document.getElementById("firmwarePerfList");
const hotspotList = document.getElementById("hotspotList");
const systemTime = document.getElementById("systemTime");
const systemPlatform = document.getElementById("systemPlatform");
const systemPython = document.getElementById("systemPython");
const systemRepo = document.getElementById("systemRepo");
const systemLogsSize = document.getElementById("systemLogsSize");
const systemBuildsSize = document.getElementById("systemBuildsSize");
const systemUnityUrl = document.getElementById("systemUnityUrl");
const systemUptime = document.getElementById("systemUptime");
const systemNotes = document.getElementById("systemNotes");
const saveNotes = document.getElementById("saveNotes");
const alertFilter = document.getElementById("alertFilter");
const clearAlerts = document.getElementById("clearAlerts");
const alertList = document.getElementById("alertList");
const alertDetail = document.getElementById("alertDetail");
const testHistoryFilter = document.getElementById("testHistoryFilter");
const clearHistory = document.getElementById("clearHistory");
const testHistory = document.getElementById("testHistory");
const bridgeReady = document.getElementById("bridgeReady");
const bridgeRunning = document.getElementById("bridgeRunning");
const bridgeNative = document.getElementById("bridgeNative");
const bridgeNativePins = document.getElementById("bridgeNativePins");
const bridgeFirmware = document.getElementById("bridgeFirmware");
const bridgeFirmwareSessions = document.getElementById(
  "bridgeFirmwareSessions"
);
const bridgeFirmwareHost = document.getElementById("bridgeFirmwareHost");
const bridgeFirmwareMode = document.getElementById("bridgeFirmwareMode");
const bridgeFirmwarePipe = document.getElementById("bridgeFirmwarePipe");
const bridgeVirtualBoards = document.getElementById("bridgeVirtualBoards");
const bridgePoweredBoards = document.getElementById("bridgePoweredBoards");
const bridgeSignals = document.getElementById("bridgeSignals");
const bridgeValidation = document.getElementById("bridgeValidation");
const bridgePhysicsRunning = document.getElementById("bridgePhysicsRunning");
const bridgePhysicsBodies = document.getElementById("bridgePhysicsBodies");
const bridgeNote = document.getElementById("bridgeNote");
const bridgeControlKeys = document.getElementById("bridgeControlKeys");
const bridgePhysicsKeys = document.getElementById("bridgePhysicsKeys");
const firmwareModeSelect = document.getElementById("firmwareModeSelect");
const firmwareModeApply = document.getElementById("firmwareModeApply");
const firmwareModeStatus = document.getElementById("firmwareModeStatus");

let unityOnline = false;
let refreshTimer = null;

const stateCache = {
  system: null,
  unity: null,
  telemetry: null,
  bridge: null,
  alerts: [],
};

const setOutput = (target, text) => {
  target.textContent = text || "";
};

const escapeHtml = (value) =>
  value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");

const fetchJson = async (url, options) => {
  const res = await fetch(url, options);
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || res.statusText);
  }
  return res.json();
};

const loadTests = async () => {
  const data = await fetchJson("/api/tests");
  testList.innerHTML = "";
  data.tests.forEach((test) => {
    const card = document.createElement("div");
    card.className = "test-card";
    card.innerHTML = `
      <h3>${test.label}</h3>
      <p>${test.description}</p>
      <button data-name="${test.name}">Run</button>
    `;
    card.querySelector("button").addEventListener("click", async () => {
      setOutput(testOutput, `Running ${test.label}...`);
      try {
        const result = await fetchJson(
          `/api/run?name=${encodeURIComponent(test.name)}`,
          {
            method: "POST",
          }
        );
        const summary = [
          `Exit code: ${result.exit_code}`,
          `Duration: ${result.duration_sec}s`,
          `Log file: ${result.log_file}`,
          "",
          result.output || "(no output)",
        ].join("\n");
        setOutput(testOutput, summary);
        pushTestHistory({
          label: test.label,
          name: test.name,
          exitCode: result.exit_code,
          duration: result.duration_sec,
          logFile: result.log_file,
          timestamp: new Date().toISOString(),
        });
      } catch (err) {
        setOutput(testOutput, `Error: ${err.message}`);
        pushTestHistory({
          label: test.label,
          name: test.name,
          exitCode: "error",
          duration: 0,
          logFile: "",
          timestamp: new Date().toISOString(),
          error: err.message,
        });
      }
    });
    testList.appendChild(card);
  });
};

const renderTestHistory = () => {
  const filter = (testHistoryFilter?.value || "").trim().toLowerCase();
  const items = loadHistory();
  testHistory.innerHTML = "";
  const filtered = items.filter((entry) => {
    if (!filter) return true;
    return (
      entry.label.toLowerCase().includes(filter) ||
      entry.name.toLowerCase().includes(filter)
    );
  });
  if (filtered.length === 0) {
    const empty = document.createElement("li");
    empty.className = "empty";
    empty.textContent = "No test history yet.";
    testHistory.appendChild(empty);
    return;
  }
  filtered.forEach((entry) => {
    const item = document.createElement("li");
    item.className = "history-item";
    item.innerHTML = `
      <div>
        <strong>${entry.label}</strong>
        <span class="tag ${entry.exitCode === 0 ? "" : "muted"}">${
      entry.exitCode
    }</span>
      </div>
      <div class="muted">${entry.timestamp} - ${entry.duration}s - ${
      entry.logFile || "no log"
    }</div>
    `;
    testHistory.appendChild(item);
  });
};

const loadLogs = async () => {
  logList.innerHTML = "";
  const area = logArea.value;
  const data = await fetchJson(`/api/logs?area=${encodeURIComponent(area)}`);
  data.logs.forEach((log) => {
    const item = document.createElement("li");
    item.innerHTML = `
      <button class="log-item" data-name="${log.name}">
        <span>${log.name}</span>
        <small>${log.modified_utc} - ${log.size} bytes</small>
      </button>
    `;
    item.querySelector("button").addEventListener("click", async () => {
      const tail = logTail.value || "400";
      try {
        const res = await fetch(
          `/api/log?area=${encodeURIComponent(area)}&name=${encodeURIComponent(
            log.name
          )}&tail=${tail}`
        );
        const text = await res.text();
        renderLogOutput(text);
      } catch (err) {
        setOutput(logOutput, `Error: ${err.message}`);
      }
    });
    logList.appendChild(item);
  });
  if (data.logs.length === 0) {
    const empty = document.createElement("li");
    empty.className = "empty";
    empty.textContent = "No logs found.";
    logList.appendChild(empty);
  }
};

const loadPorts = async () => {
  portList.innerHTML = "";
  try {
    let bridge = null;
    try {
      bridge = await fetchJson("/api/bridge-status");
    } catch {
      bridge = null;
    }

    const mapping = bridge?.virtual_com;
    const mappedPairs = Array.isArray(mapping?.pairs) ? mapping.pairs : [];
    if (mappedPairs.length > 0) {
      const header = document.createElement("li");
      header.innerHTML = `
        <div class="port-item">
          <div>
            <strong>Virtual Boards (from Unity)</strong>
            <div class="muted">port_base: ${mapping.port_base ?? "N/A"} | ${
        mapping.status ?? ""
      }</div>
          </div>
        </div>
      `;
      portList.appendChild(header);

      mappedPairs.forEach((pair) => {
        const item = document.createElement("li");
        const usbTag = pair.usb_connected
          ? '<span class="tag">usb</span>'
          : '<span class="tag muted">usb off</span>';
        item.innerHTML = `
          <div class="port-item">
            <div>
              <strong>${escapeHtml(
                pair.board_id || "UNKNOWN"
              )}</strong> ${usbTag}
              <div class="muted">IDE: ${escapeHtml(
                pair.ide_port || "-"
              )} / APP: ${escapeHtml(pair.app_port || "-")}</div>
            </div>
          </div>
        `;
        portList.appendChild(item);
      });
    } else {
      const header = document.createElement("li");
      header.className = "empty";
      header.textContent = bridge
        ? "No virtual COM mappings reported by Unity."
        : "Unity not connected (virtual COM mapping unavailable).";
      portList.appendChild(header);
    }

    const data = await fetchJson("/api/com-ports");
    if (!data.ports || data.ports.length === 0) {
      const empty = document.createElement("li");
      empty.className = "empty";
      empty.textContent = "No COM ports detected.";
      portList.appendChild(empty);
      return;
    }
    data.ports.forEach((port) => {
      const item = document.createElement("li");
      const tag = port.is_virtual ? '<span class="tag">virtual</span>' : "";
      item.innerHTML = `
        <div class="port-item">
          <div>
            <strong>${port.device_id || "UNKNOWN"}</strong> ${tag}
            <div class="muted">${port.name || port.description || ""}</div>
          </div>
          <div class="muted">${port.pnp_device_id || ""}</div>
        </div>
      `;
      portList.appendChild(item);
    });
  } catch (err) {
    const error = document.createElement("li");
    error.className = "empty";
    error.textContent = `Error: ${err.message}`;
    portList.appendChild(error);
  }
};

const formatValue = (value, unit) => {
  if (value === null || value === undefined) return "N/A";
  if (Number.isNaN(value)) return "N/A";
  if (typeof value !== "number") return `${value}${unit}`;
  return `${value.toFixed(3)}${unit}`;
};

const renderTelemetryList = (components) => {
  const filter = (telemetryFilter.value || "").trim().toLowerCase();
  telemetryList.innerHTML = "";
  const filtered = components.filter((comp) => {
    if (!filter) return true;
    return (
      comp.id.toLowerCase().includes(filter) ||
      (comp.type || "").toLowerCase().includes(filter)
    );
  });

  if (filtered.length === 0) {
    const empty = document.createElement("li");
    empty.className = "empty";
    empty.textContent = "No telemetry items found.";
    telemetryList.appendChild(empty);
    return;
  }

  filtered.forEach((comp) => {
    const values = comp.values || {};
    const lines = [
      `V: ${formatValue(values.v, "V")}`,
      `I: ${formatValue(values.i, "A")}`,
      `P: ${formatValue(values.p, "W")}`,
      `T: ${formatValue(values.t, "C")}`,
      `R: ${formatValue(values.r, " Ohm")}`,
      `L: ${formatValue(values.l, "")}`,
      `SRC V: ${formatValue(values.src_v, "V")}`,
      `SRC I: ${formatValue(values.src_i, "A")}`,
      `SOC: ${formatValue(values.soc, "%")}`,
      `Rint: ${formatValue(values.rint, " Ohm")}`,
    ];

    const item = document.createElement("li");
    item.innerHTML = `
      <div class="telemetry-item">
        <div>
          <strong>${comp.id}</strong>
          <span class="tag">${comp.type || "Component"}</span>
          ${
            comp.powered
              ? '<span class="tag">powered</span>'
              : '<span class="tag muted">off</span>'
          }
        </div>
        <div class="telemetry-values">${lines.join(" | ")}</div>
      </div>
    `;
    telemetryList.appendChild(item);
  });
};

const renderFirmwarePerfList = (items) => {
  firmwarePerfList.innerHTML = "";
  if (!items || items.length === 0) {
    const empty = document.createElement("li");
    empty.className = "empty";
    empty.textContent = "No firmware perf data.";
    firmwarePerfList.appendChild(empty);
    return;
  }

  items.forEach((board) => {
    const metrics = board.metrics || {};
    const lines = [
      `cycles: ${metrics.cycles ?? "N/A"}`,
      `adc: ${metrics.adc_samples ?? "N/A"}`,
      `uart tx: ${metrics.uart_tx0 ?? 0}/${metrics.uart_tx1 ?? 0}/${
        metrics.uart_tx2 ?? 0
      }/${metrics.uart_tx3 ?? 0}`,
      `uart rx: ${metrics.uart_rx0 ?? 0}/${metrics.uart_rx1 ?? 0}/${
        metrics.uart_rx2 ?? 0
      }/${metrics.uart_rx3 ?? 0}`,
      `spi: ${metrics.spi_transfers ?? 0}`,
      `twi: ${metrics.twi_transfers ?? 0}`,
      `wdt: ${metrics.wdt_resets ?? 0}`,
      `drops: ${metrics.drops ?? 0}`,
    ];
    const item = document.createElement("li");
    item.innerHTML = `
      <div class="telemetry-item">
        <div>
          <strong>${board.id || "board"}</strong>
          <span class="tag">firmware</span>
        </div>
        <div class="telemetry-values">${lines.join(" | ")}</div>
      </div>
    `;
    firmwarePerfList.appendChild(item);
  });
};

const renderHotspots = (components) => {
  hotspotList.innerHTML = "";
  if (!components || components.length === 0) {
    hotspotList.innerHTML = '<li class="empty">No telemetry yet.</li>';
    return;
  }
  const withTemp = components
    .map((comp) => ({
      id: comp.id,
      type: comp.type || "Component",
      temp: comp.values?.t ?? null,
      power: comp.values?.p ?? null,
    }))
    .filter((comp) => comp.temp !== null || comp.power !== null);
  if (withTemp.length === 0) {
    hotspotList.innerHTML = '<li class="empty">No hotspot data.</li>';
    return;
  }
  const topTemp = [...withTemp]
    .sort((a, b) => (b.temp ?? -Infinity) - (a.temp ?? -Infinity))
    .slice(0, 5);
  topTemp.forEach((comp) => {
    const item = document.createElement("li");
    item.className = "telemetry-item";
    item.innerHTML = `
      <div>
        <strong>${comp.id}</strong>
        <span class="tag">${comp.type}</span>
      </div>
      <div class="telemetry-values">Temp: ${formatValue(
        comp.temp ?? NaN,
        "C"
      )} | Power: ${formatValue(comp.power ?? NaN, "W")}</div>
    `;
    hotspotList.appendChild(item);
  });
};

const loadUnityStatus = async () => {
  const start = performance.now();
  try {
    const data = await fetchJson("/api/unity-status");
    unityOnline = !!data.connected;
    unityConnected.textContent = unityOnline ? "Connected" : "Disconnected";
    engineStatus.textContent = data.status?.engine || "Unknown";
    sceneName.textContent = data.scene || "N/A";
    runMode.textContent =
      data.run_mode === true
        ? "true"
        : data.run_mode === false
        ? "false"
        : "N/A";
    stateCache.unity = data;
    if (unityLatency) {
      const delta = performance.now() - start;
      unityLatency.textContent = `${delta.toFixed(0)} ms`;
    }
  } catch (err) {
    unityOnline = false;
    unityConnected.textContent = "Error";
    engineStatus.textContent = "N/A";
    sceneName.textContent = "N/A";
    runMode.textContent = "N/A";
    if (unityLatency) {
      unityLatency.textContent = "N/A";
    }
  }
};

const loadTelemetry = async () => {
  if (!unityOnline) {
    simRunning.textContent = "false";
    simTick.textContent = "0";
    simTime.textContent = "0.00";
    signalCount.textContent = "0";
    validationCount.textContent = "0";
    simAvgJitter.textContent = "N/A";
    simMaxJitter.textContent = "N/A";
    simAvgTick.textContent = "N/A";
    simOverruns.textContent = "0";
    simFastPath.textContent = "0";
    simCorrective.textContent = "0";
    simBudgetOverruns.textContent = "0";
    telemetryList.innerHTML = '<li class="empty">Unity not connected.</li>';
    firmwarePerfList.innerHTML = '<li class="empty">Unity not connected.</li>';
    hotspotList.innerHTML = '<li class="empty">Unity not connected.</li>';
    return;
  }
  try {
    const data = await fetchJson("/api/unity-telemetry");
    simRunning.textContent = data.running ? "true" : "false";
    simTick.textContent = data.tick ?? 0;
    simTime.textContent = data.time ?? 0;
    signalCount.textContent = data.signals ?? 0;
    validationCount.textContent = data.validation ?? 0;
    const timing = data.timing || {};
    const realtimeStats = data.realtime_stats || {};
    simAvgJitter.textContent = Number.isFinite(timing.avg_jitter_ms)
      ? timing.avg_jitter_ms.toFixed(3)
      : "N/A";
    simMaxJitter.textContent = Number.isFinite(timing.max_jitter_ms)
      ? timing.max_jitter_ms.toFixed(3)
      : "N/A";
    simAvgTick.textContent = Number.isFinite(timing.avg_tick_ms)
      ? timing.avg_tick_ms.toFixed(3)
      : "N/A";
    simOverruns.textContent = timing.overruns ?? 0;
    simFastPath.textContent = realtimeStats.fast_path ?? 0;
    simCorrective.textContent = realtimeStats.corrective ?? 0;
    simBudgetOverruns.textContent = realtimeStats.budget_overruns ?? 0;
    renderTelemetryList(data.components || []);
    renderFirmwarePerfList(data.firmware || []);
    renderHotspots(data.components || []);
    stateCache.telemetry = data;
  } catch (err) {
    simRunning.textContent = "N/A";
    simTick.textContent = "0";
    simTime.textContent = "0.00";
    signalCount.textContent = "0";
    validationCount.textContent = "0";
    simAvgJitter.textContent = "N/A";
    simMaxJitter.textContent = "N/A";
    simAvgTick.textContent = "N/A";
    simOverruns.textContent = "0";
    simFastPath.textContent = "0";
    simCorrective.textContent = "0";
    simBudgetOverruns.textContent = "0";
    telemetryList.innerHTML =
      '<li class="empty">Unity telemetry not available.</li>';
    firmwarePerfList.innerHTML =
      '<li class="empty">Unity telemetry not available.</li>';
    hotspotList.innerHTML =
      '<li class="empty">Unity telemetry not available.</li>';
  }
};

const loadBridgeStatus = async () => {
  if (!unityOnline) {
    bridgeReady.textContent = "false";
    bridgeRunning.textContent = "false";
    bridgeNative.textContent = "false";
    bridgeNativePins.textContent = "false";
    bridgeFirmware.textContent = "false";
    bridgeFirmwareSessions.textContent = "0";
    bridgeFirmwareHost.textContent = "N/A";
    bridgeFirmwareMode.textContent = "N/A";
    bridgeFirmwarePipe.textContent = "N/A";
    bridgeVirtualBoards.textContent = "0";
    bridgePoweredBoards.textContent = "0";
    bridgeSignals.textContent = "0";
    bridgeValidation.textContent = "0";
    bridgePhysicsRunning.textContent = "false";
    bridgePhysicsBodies.textContent = "0";
    bridgeControlKeys.innerHTML = "";
    bridgePhysicsKeys.innerHTML = "";
    bridgeNote.textContent = "Unity not connected.";
    if (firmwareModeStatus) {
      firmwareModeStatus.textContent = "Unity not connected.";
    }
    return;
  }

  try {
    const data = await fetchJson("/api/bridge-status");
    bridgeReady.textContent = data.ready ? "true" : "false";
    bridgeRunning.textContent = data.running ? "true" : "false";
    bridgeNative.textContent = data.use_native ? "true" : "false";
    bridgeNativePins.textContent = data.native_ready ? "true" : "false";
    bridgeFirmware.textContent = data.use_firmware ? "true" : "false";
    bridgeFirmwareSessions.textContent = data.external_firmware_sessions ?? 0;
    bridgeFirmwareHost.textContent = data.firmware_host || "N/A";
    bridgeFirmwareMode.textContent = data.firmware_mode || "N/A";
    bridgeFirmwarePipe.textContent = data.firmware_pipe || "N/A";
    bridgeVirtualBoards.textContent = data.virtual_boards ?? 0;
    bridgePoweredBoards.textContent = data.powered_boards ?? 0;
    bridgeSignals.textContent = data.signals ?? 0;
    bridgeValidation.textContent = data.validation ?? 0;
    bridgePhysicsRunning.textContent = data.physics_running ? "true" : "false";
    bridgePhysicsBodies.textContent = data.physics_bodies ?? 0;
    const control = data.contract?.control || [];
    const physics = data.contract?.physics || [];
    bridgeControlKeys.innerHTML =
      control.map((key) => `<li>${key}</li>`).join("") || "<li>N/A</li>";
    bridgePhysicsKeys.innerHTML =
      physics.map((key) => `<li>${key}</li>`).join("") || "<li>N/A</li>";
    bridgeNote.textContent = "Live bridge status from Unity.";
    if (firmwareModeSelect) {
      const modeValue = (data.firmware_mode || "").toLowerCase();
      if (modeValue === "lockstep" || modeValue === "realtime") {
        firmwareModeSelect.value = modeValue;
      }
    }
    if (firmwareModeStatus) {
      firmwareModeStatus.textContent = "";
    }
    stateCache.bridge = data;
  } catch (err) {
    bridgeNote.textContent = "Bridge status unavailable.";
  }
};

const loadSystemInfo = async () => {
  try {
    const data = await fetchJson("/api/system-info");
    stateCache.system = data;
    systemTime.textContent = data.server_time || "N/A";
    systemPlatform.textContent = data.platform || "N/A";
    systemPython.textContent = data.python || "N/A";
    systemRepo.textContent = data.repo || "N/A";
    systemLogsSize.textContent = data.logs_size || "N/A";
    systemBuildsSize.textContent = data.builds_size || "N/A";
    systemUnityUrl.textContent = data.unity_base_url || "N/A";
    systemUptime.textContent = data.uptime || "N/A";
  } catch (err) {
    systemTime.textContent = "N/A";
    systemPlatform.textContent = "N/A";
    systemPython.textContent = "N/A";
    systemRepo.textContent = "N/A";
    systemLogsSize.textContent = "N/A";
    systemBuildsSize.textContent = "N/A";
    systemUnityUrl.textContent = "N/A";
    systemUptime.textContent = "N/A";
  }
};

const loadAlerts = async () => {
  const tail = logScanTail?.value || "400";
  try {
    const data = await fetchJson(
      `/api/log-scan?tail=${encodeURIComponent(tail)}`
    );
    stateCache.alerts = data.alerts || [];
    renderAlerts(stateCache.alerts);
  } catch (err) {
    alertList.innerHTML = `<li class="empty">Error: ${err.message}</li>`;
  }
};

const renderAlerts = (items) => {
  const filter = (alertFilter?.value || "").trim().toLowerCase();
  alertList.innerHTML = "";
  const filtered = items.filter((entry) => {
    if (!filter) return true;
    return (
      entry.message.toLowerCase().includes(filter) ||
      entry.area.toLowerCase().includes(filter) ||
      entry.log.toLowerCase().includes(filter)
    );
  });
  if (filtered.length === 0) {
    const empty = document.createElement("li");
    empty.className = "empty";
    empty.textContent = "No alerts found.";
    alertList.appendChild(empty);
    return;
  }
  filtered.forEach((entry) => {
    const item = document.createElement("li");
    item.className = `alert-item ${entry.level}`;
    item.innerHTML = `
      <div>
        <strong>${entry.level.toUpperCase()}</strong>
        <span class="tag">${entry.area}</span>
        <span class="tag muted">${entry.log}</span>
      </div>
      <div class="muted">${entry.timestamp}</div>
      <div>${escapeHtml(entry.message)}</div>
    `;
    item.addEventListener("click", async () => {
      const tail = logTail?.value || "200";
      const res = await fetch(
        `/api/log?area=${encodeURIComponent(
          entry.area
        )}&name=${encodeURIComponent(entry.log)}&tail=${tail}`
      );
      const text = await res.text();
      renderLogOutput(text, entry.message);
      alertDetail.textContent = entry.message;
    });
    alertList.appendChild(item);
  });
};

const renderLogOutput = (text, highlight) => {
  const query = (logSearch?.value || highlight || "").trim();
  if (!query) {
    logOutput.textContent = text || "(empty)";
    return;
  }
  const escaped = escapeHtml(text || "");
  const safeQuery = escapeHtml(query);
  const regex = new RegExp(
    safeQuery.replace(/[.*+?^${}()|[\]\\]/g, "\\$&"),
    "gi"
  );
  const highlighted = escaped.replace(
    regex,
    (match) => `<mark>${match}</mark>`
  );
  logOutput.innerHTML = highlighted || "(empty)";
};

const pushTestHistory = (entry) => {
  const items = loadHistory();
  items.unshift(entry);
  const trimmed = items.slice(0, 50);
  localStorage.setItem("debugConsoleHistory", JSON.stringify(trimmed));
  renderTestHistory();
};

const loadHistory = () => {
  try {
    const raw = localStorage.getItem("debugConsoleHistory");
    if (!raw) return [];
    const parsed = JSON.parse(raw);
    return Array.isArray(parsed) ? parsed : [];
  } catch (err) {
    return [];
  }
};

const loadNotes = () => {
  if (!systemNotes) return;
  systemNotes.value = localStorage.getItem("debugConsoleNotes") || "";
};

const saveNotesToStorage = () => {
  if (!systemNotes) return;
  localStorage.setItem("debugConsoleNotes", systemNotes.value || "");
};

const updateLastUpdate = () => {
  if (!lastUpdate) return;
  const now = new Date();
  lastUpdate.textContent = now.toLocaleTimeString();
};

const downloadSnapshotJson = () => {
  const snapshot = {
    captured_at: new Date().toISOString(),
    system: stateCache.system,
    unity: stateCache.unity,
    telemetry: stateCache.telemetry,
    bridge: stateCache.bridge,
    alerts: stateCache.alerts,
    notes: systemNotes?.value || "",
  };
  const blob = new Blob([JSON.stringify(snapshot, null, 2)], {
    type: "application/json",
  });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = `debug_snapshot_${Date.now()}.json`;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
};

const runRefreshLoop = () => {
  if (refreshTimer) {
    clearInterval(refreshTimer);
  }
  const interval = Math.max(
    2,
    Math.min(30, Number(refreshIntervalInput?.value || 4))
  );
  if (autoRefreshToggle?.checked === false) {
    return;
  }
  refreshTimer = setInterval(() => {
    loadUnityStatus().then(() => {
      loadTelemetry();
      loadBridgeStatus();
    });
    loadSystemInfo();
    loadAlerts();
    updateLastUpdate();
  }, interval * 1000);
};

if (firmwareModeApply) {
  firmwareModeApply.addEventListener("click", async () => {
    if (!firmwareModeSelect) return;
    const mode = firmwareModeSelect.value || "lockstep";
    firmwareModeStatus.textContent = "Applying...";
    try {
      const result = await fetchJson(
        `/api/firmware-mode?mode=${encodeURIComponent(mode)}`
      );
      if (result.requires_restart) {
        firmwareModeStatus.textContent = "Mode set. Restart firmware to apply.";
      } else {
        firmwareModeStatus.textContent = "Mode applied.";
      }
      bridgeFirmwareMode.textContent = result.mode || mode;
    } catch (err) {
      firmwareModeStatus.textContent = `Error: ${err.message}`;
    }
  });
}

clearOutput.addEventListener("click", () => setOutput(testOutput, ""));
refreshLogs.addEventListener("click", loadLogs);
logArea.addEventListener("change", loadLogs);
refreshPorts.addEventListener("click", loadPorts);
refreshTelemetry.addEventListener("click", () => {
  loadUnityStatus().then(() => {
    loadTelemetry();
    loadBridgeStatus();
  });
  loadSystemInfo();
});
telemetryFilter.addEventListener("input", () => {
  loadTelemetry();
});
logSearch?.addEventListener("input", () => {
  renderLogOutput(logOutput.textContent || "");
});
refreshAlerts?.addEventListener("click", () => {
  loadAlerts();
});
alertFilter?.addEventListener("input", () => {
  renderAlerts(stateCache.alerts);
});
clearAlerts?.addEventListener("click", () => {
  stateCache.alerts = [];
  renderAlerts([]);
});
clearHistory?.addEventListener("click", () => {
  localStorage.removeItem("debugConsoleHistory");
  renderTestHistory();
});
testHistoryFilter?.addEventListener("input", () => {
  renderTestHistory();
});
saveNotes?.addEventListener("click", () => {
  saveNotesToStorage();
});
downloadSnapshot?.addEventListener("click", () => {
  downloadSnapshotJson();
});
autoRefreshToggle?.addEventListener("change", () => {
  runRefreshLoop();
});
refreshIntervalInput?.addEventListener("change", () => {
  runRefreshLoop();
});

loadTests().catch((err) => setOutput(testOutput, `Error: ${err.message}`));
loadLogs().catch((err) => setOutput(logOutput, `Error: ${err.message}`));
loadPorts();
loadNotes();
renderTestHistory();
loadSystemInfo();
loadAlerts();
loadUnityStatus().then(() => {
  loadTelemetry();
  loadBridgeStatus();
  updateLastUpdate();
});
runRefreshLoop();
