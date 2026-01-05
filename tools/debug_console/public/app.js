const testList = document.getElementById("testList");
const testOutput = document.getElementById("testOutput");
const clearOutput = document.getElementById("clearOutput");
const logArea = document.getElementById("logArea");
const logTail = document.getElementById("logTail");
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
let unityOnline = false;
const bridgeReady = document.getElementById("bridgeReady");
const bridgeRunning = document.getElementById("bridgeRunning");
const bridgeNative = document.getElementById("bridgeNative");
const bridgeNativePins = document.getElementById("bridgeNativePins");
const bridgeFirmware = document.getElementById("bridgeFirmware");
const bridgeFirmwareSessions = document.getElementById("bridgeFirmwareSessions");
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

const setOutput = (target, text) => {
  target.textContent = text || "";
};

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
        const result = await fetchJson(`/api/run?name=${encodeURIComponent(test.name)}`, {
          method: "POST",
        });
        const summary = [
          `Exit code: ${result.exit_code}`,
          `Duration: ${result.duration_sec}s`,
          `Log file: ${result.log_file}`,
          "",
          result.output || "(no output)",
        ].join("\n");
        setOutput(testOutput, summary);
      } catch (err) {
        setOutput(testOutput, `Error: ${err.message}`);
      }
    });
    testList.appendChild(card);
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
        <small>${log.modified_utc} · ${log.size} bytes</small>
      </button>
    `;
    item.querySelector("button").addEventListener("click", async () => {
      const tail = logTail.value || "400";
      try {
        const res = await fetch(
          `/api/log?area=${encodeURIComponent(area)}&name=${encodeURIComponent(log.name)}&tail=${tail}`
        );
        const text = await res.text();
        setOutput(logOutput, text || "(empty)");
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
      const tag = port.is_virtual ? "<span class=\"tag\">virtual</span>" : "";
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
      `R: ${formatValue(values.r, "Ω")}`,
      `L: ${formatValue(values.l, "")}`,
      `SRC V: ${formatValue(values.src_v, "V")}`,
      `SRC I: ${formatValue(values.src_i, "A")}`,
      `SOC: ${formatValue(values.soc, "%")}`,
      `Rint: ${formatValue(values.rint, "Ω")}`,
    ];

    const item = document.createElement("li");
    item.innerHTML = `
      <div class="telemetry-item">
        <div>
          <strong>${comp.id}</strong>
          <span class="tag">${comp.type || "Component"}</span>
          ${comp.powered ? "<span class=\"tag\">powered</span>" : "<span class=\"tag muted\">off</span>"}
        </div>
        <div class="telemetry-values">${lines.join(" · ")}</div>
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
      `uart tx: ${metrics.uart_tx0 ?? 0}/${metrics.uart_tx1 ?? 0}/${metrics.uart_tx2 ?? 0}/${metrics.uart_tx3 ?? 0}`,
      `uart rx: ${metrics.uart_rx0 ?? 0}/${metrics.uart_rx1 ?? 0}/${metrics.uart_rx2 ?? 0}/${metrics.uart_rx3 ?? 0}`,
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
        <div class="telemetry-values">${lines.join(" · ")}</div>
      </div>
    `;
    firmwarePerfList.appendChild(item);
  });
};

const loadUnityStatus = async () => {
  try {
    const data = await fetchJson("/api/unity-status");
    unityOnline = !!data.connected;
    unityConnected.textContent = unityOnline ? "Connected" : "Disconnected";
    engineStatus.textContent = data.status?.engine || "Unknown";
    sceneName.textContent = data.scene || "N/A";
    runMode.textContent = data.run_mode === true ? "true" : data.run_mode === false ? "false" : "N/A";
  } catch (err) {
    unityOnline = false;
    unityConnected.textContent = "Error";
    engineStatus.textContent = "N/A";
    sceneName.textContent = "N/A";
    runMode.textContent = "N/A";
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
    telemetryList.innerHTML = "<li class=\"empty\">Unity not connected.</li>";
    firmwarePerfList.innerHTML = "<li class=\"empty\">Unity not connected.</li>";
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
    telemetryList.innerHTML = "<li class=\"empty\">Unity telemetry not available.</li>";
    firmwarePerfList.innerHTML = "<li class=\"empty\">Unity telemetry not available.</li>";
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
    bridgeControlKeys.innerHTML = control.map((key) => `<li>${key}</li>`).join("") || "<li>N/A</li>";
    bridgePhysicsKeys.innerHTML = physics.map((key) => `<li>${key}</li>`).join("") || "<li>N/A</li>";
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
  } catch (err) {
    bridgeNote.textContent = "Bridge status unavailable.";
  }
};

if (firmwareModeApply) {
  firmwareModeApply.addEventListener("click", async () => {
    if (!firmwareModeSelect) return;
    const mode = firmwareModeSelect.value || "lockstep";
    firmwareModeStatus.textContent = "Applying...";
    try {
      const result = await fetchJson(`/api/firmware-mode?mode=${encodeURIComponent(mode)}`);
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
});
telemetryFilter.addEventListener("input", () => {
  loadTelemetry();
});

loadTests().catch((err) => setOutput(testOutput, `Error: ${err.message}`));
loadLogs().catch((err) => setOutput(logOutput, `Error: ${err.message}`));
loadPorts();
loadUnityStatus().then(() => {
  loadTelemetry();
  loadBridgeStatus();
});
setInterval(() => {
  loadUnityStatus().then(() => {
    loadTelemetry();
    loadBridgeStatus();
  });
}, 4000);
