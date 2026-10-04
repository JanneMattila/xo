const state = {
  components: [],
  humanGame: null,
  busy: false,
  run: null,
  runTimer: null,
  history: [],
  selectedGame: null,
  replayIndex: 0,
  humanClock: null,
  pendingHumanMove: null,
  activeView: "play"
};

const elements = Object.fromEntries([
  "connection-status", "human-ai", "new-human-game", "human-status", "human-turn",
  "human-move-count", "human-x-time", "human-o-time", "human-setup", "human-board", "arena-form", "player1", "player2", "arena-games",
  "alternate-sides", "arena-opening-set", "run-status", "run-progress", "stat-games", "stat-player1",
  "stat-player2", "stat-draws", "stat-moves", "stat-rate", "arena-statistics", "run-error", "history-list",
  "replay-status", "replay-players", "replay-x-time", "replay-o-time", "replay-step", "replay-board", "replay-slider"
].map(id => [id, document.getElementById(id)]));

const boardResizeObserver = new ResizeObserver(entries => {
  entries.forEach(entry => entry.target.drawBoard?.());
});
boardResizeObserver.observe(elements["human-board"]);
boardResizeObserver.observe(elements["replay-board"]);

document.querySelectorAll(".tab").forEach(button => {
  button.addEventListener("click", () => showView(button.dataset.view));
});
elements["new-human-game"].addEventListener("click", createHumanGame);
elements["arena-form"].addEventListener("submit", startAiRun);
elements["replay-slider"].addEventListener("input", event => {
  state.replayIndex = Number(event.target.value);
  renderReplay();
});
window.addEventListener("keydown", handleReplayKeys);
setInterval(renderHumanTimes, 100);

initialize();

async function initialize() {
  try {
    state.components = await api("/api/components");
    populateComponents();
    elements["connection-status"].textContent = "Ready";
    renderBoard(elements["human-board"], [], false, null);
    renderBoard(elements["replay-board"], [], false, null);
    await refreshHistory();
  } catch (error) {
    elements["connection-status"].textContent = "Unavailable";
    elements["human-status"].textContent = error.message;
  }
}

function populateComponents() {
  const options = state.components.map(component =>
    `<option value="${escapeHtml(component.id)}">${escapeHtml(component.displayName)}</option>`
  ).join("");
  elements["human-ai"].innerHTML = options;
  elements.player1.innerHTML = options;
  elements.player2.innerHTML = options;
}

function showView(view) {
  state.activeView = view;
  document.querySelectorAll(".tab").forEach(tab => tab.classList.toggle("is-active", tab.dataset.view === view));
  document.querySelectorAll(".view").forEach(section => {
    const active = section.id === `view-${view}`;
    section.hidden = !active;
    section.classList.toggle("is-active", active);
  });
  if (view === "archive") refreshHistory();
}

async function createHumanGame() {
  if (state.busy || !elements["human-ai"].value) return;
  state.pendingHumanMove = null;
  setBusy(true);
  try {
    const humanMark = document.querySelector('input[name="human-mark"]:checked').value;
    state.humanGame = await api("/api/human-games", {
      method: "POST",
      body: JSON.stringify({ aiId: elements["human-ai"].value, humanMark })
    });
    elements["human-setup"].open = false;
    syncHumanClock(state.humanGame);
    renderHumanGame();
  } catch (error) {
    elements["human-status"].textContent = error.message;
  } finally {
    setBusy(false);
  }
}

async function playHumanMove(x, y) {
  if (state.busy || !state.humanGame || state.humanGame.status !== "InProgress") return;
  if (state.humanGame.nextMark !== state.humanGame.humanMark) return;
  const previousGame = state.humanGame;
  const previousClock = state.humanClock ? { ...state.humanClock } : null;
  const aiMark = previousGame.humanMark === "X" ? "O" : "X";
  let requestError = null;

  state.pendingHumanMove = null;
  beginHumanClockTurn(aiMark);
  state.humanGame = {
    ...previousGame,
    nextMark: aiMark,
    moves: [...previousGame.moves, {
      number: previousGame.moves.length + 1,
      x,
      y,
      mark: previousGame.humanMark,
      elapsedMilliseconds: 0
    }]
  };
  setBusy(true);
  elements["human-status"].textContent = "AI is thinking";
  try {
    state.humanGame = await api(`/api/human-games/${previousGame.id}/moves`, {
      method: "POST",
      body: JSON.stringify({ x, y })
    });
    syncHumanClock(state.humanGame);
    renderHumanGame();
    if (state.humanGame.status !== "InProgress") await refreshHistory();
  } catch (error) {
    state.humanGame = previousGame;
    state.humanClock = previousClock;
    state.pendingHumanMove = { x, y };
    requestError = error.message;
  } finally {
    setBusy(false);
    if (requestError) elements["human-status"].textContent = requestError;
  }
}

function handleHumanCellClick(x, y) {
  if (state.busy || !state.humanGame || state.humanGame.status !== "InProgress") return;
  if (state.humanGame.nextMark !== state.humanGame.humanMark) return;

  if (state.pendingHumanMove?.x === x && state.pendingHumanMove?.y === y) {
    void playHumanMove(x, y);
    return;
  }

  state.pendingHumanMove = { x, y };
  renderHumanGame();
}

function renderHumanGame() {
  const game = state.humanGame;
  if (!game) return;
  const canPlay = game.status === "InProgress" && game.nextMark === game.humanMark && !state.busy;
  renderBoard(
    elements["human-board"],
    game.moves,
    canPlay,
    handleHumanCellClick,
    game,
    game.moves.length,
    state.pendingHumanMove);
  elements["human-move-count"].textContent = game.moves.length;
  elements["human-turn"].textContent = game.status === "InProgress" ? game.nextMark : "—";
  elements["human-status"].textContent = state.pendingHumanMove
    ? `Confirm ${game.humanMark} at ${state.pendingHumanMove.x}, ${state.pendingHumanMove.y}`
    : statusText(game);
}

function renderBoard(
  canvas,
  moves,
  interactive,
  onCell,
  game = null,
  throughMove = moves.length,
  selectedPosition = null) {
  const width = game?.width ?? 20;
  const height = game?.height ?? 20;
  const originX = game?.originX ?? 0;
  const originY = game?.originY ?? 0;
  const shownMoves = moves.slice(0, throughMove);
  const occupied = new Map(shownMoves.map(move => [`${move.x},${move.y}`, move]));
  const selectedText = selectedPosition
    ? `, ${game.humanMark} selected at ${selectedPosition.x}, ${selectedPosition.y}; click again to confirm`
    : "";

  canvas.style.aspectRatio = `${width} / ${height}`;
  canvas.classList.toggle("is-interactive", interactive);
  canvas.setAttribute("aria-label", `XO board, ${shownMoves.length} moves${selectedText}`);
  canvas.onclick = interactive && onCell ? event => {
    const bounds = canvas.getBoundingClientRect();
    const column = Math.floor(((event.clientX - bounds.left) / bounds.width) * width);
    const row = Math.floor(((event.clientY - bounds.top) / bounds.height) * height);
    if (column < 0 || column >= width || row < 0 || row >= height) return;

    const x = originX + column;
    const y = originY + row;
    if (!occupied.has(`${x},${y}`)) onCell(x, y);
  } : null;
  canvas.onkeydown = interactive && onCell && selectedPosition ? event => {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    onCell(selectedPosition.x, selectedPosition.y);
  } : null;

  const draw = () => drawBoard(canvas, {
    width,
    height,
    originX,
    originY,
    shownMoves,
    selectedPosition,
    selectedMark: game?.humanMark
  });
  canvas.drawBoard = draw;
  draw();
}

function drawBoard(canvas, board) {
  const cssWidth = canvas.clientWidth;
  if (cssWidth === 0) return;

  const pixelRatio = window.devicePixelRatio || 1;
  const cellSize = Math.max(1, Math.round((cssWidth * pixelRatio) / board.width));
  const pixelWidth = cellSize * board.width;
  const pixelHeight = cellSize * board.height;
  if (canvas.width !== pixelWidth) canvas.width = pixelWidth;
  if (canvas.height !== pixelHeight) canvas.height = pixelHeight;

  const context = canvas.getContext("2d");
  const styles = getComputedStyle(document.documentElement);
  const forest = styles.getPropertyValue("--forest").trim();
  const coral = styles.getPropertyValue("--coral").trim();
  const forestBright = styles.getPropertyValue("--forest-bright").trim();
  const last = board.shownMoves.at(-1);

  context.fillStyle = "#fbfcfb";
  context.fillRect(0, 0, pixelWidth, pixelHeight);
  if (last) {
    context.fillStyle = "#fff1bf";
    fillBoardCell(context, last.x, last.y, board, cellSize);
  }
  if (board.selectedPosition) {
    context.fillStyle = "#dcece4";
    fillBoardCell(context, board.selectedPosition.x, board.selectedPosition.y, board, cellSize);
  }

  const lineWidth = Math.max(1, Math.round(pixelRatio));
  context.beginPath();
  for (let column = 1; column < board.width; column++) {
    context.moveTo(column * cellSize, 0);
    context.lineTo(column * cellSize, pixelHeight);
  }
  for (let row = 1; row < board.height; row++) {
    context.moveTo(0, row * cellSize);
    context.lineTo(pixelWidth, row * cellSize);
  }
  context.strokeStyle = "#c3ccc6";
  context.lineWidth = lineWidth;
  context.stroke();

  if (board.selectedPosition) {
    const column = board.selectedPosition.x - board.originX;
    const row = board.selectedPosition.y - board.originY;
    context.strokeStyle = forestBright;
    context.lineWidth = Math.max(2, Math.round(pixelRatio * 2));
    context.strokeRect(
      (column * cellSize) + context.lineWidth / 2,
      (row * cellSize) + context.lineWidth / 2,
      cellSize - context.lineWidth,
      cellSize - context.lineWidth);
  }

  board.shownMoves.forEach(move => drawStone(context, move, board, cellSize, forest, coral, false));
  if (board.selectedPosition && board.selectedMark) {
    drawStone(
      context,
      { ...board.selectedPosition, mark: board.selectedMark },
      board,
      cellSize,
      forest,
      coral,
      true);
  }
}

function fillBoardCell(context, x, y, board, cellSize) {
  context.fillRect(
    (x - board.originX) * cellSize,
    (y - board.originY) * cellSize,
    cellSize,
    cellSize);
}

function drawStone(context, move, board, cellSize, forest, coral, pending) {
  const centerX = ((move.x - board.originX) + 0.5) * cellSize;
  const centerY = ((move.y - board.originY) + 0.5) * cellSize;
  context.save();
  context.globalAlpha = pending ? 0.48 : 1;
  context.beginPath();
  context.arc(centerX, centerY, cellSize * 0.37, 0, Math.PI * 2);
  context.fillStyle = move.mark === "X" ? forest : coral;
  context.fill();
  context.fillStyle = "#fff";
  context.font = `700 ${Math.max(10, cellSize * 0.45)}px Georgia, serif`;
  context.textAlign = "center";
  context.textBaseline = "middle";
  context.fillText(move.mark, centerX, centerY + (cellSize * 0.02));
  context.restore();
}

async function startAiRun(event) {
  event.preventDefault();
  if (state.run && ["queued", "running"].includes(state.run.status)) return;
  elements["run-error"].textContent = "";
  try {
    state.run = await api("/api/ai-runs", {
      method: "POST",
      body: JSON.stringify({
        player1: elements.player1.value,
        player2: elements.player2.value,
        games: Number(elements["arena-games"].value),
        alternateSides: elements["alternate-sides"].checked,
        openingSet: elements["arena-opening-set"].value
      })
    });
    elements["arena-statistics"].open = false;
    renderRun();
    clearInterval(state.runTimer);
    state.runTimer = setInterval(pollRun, 750);
  } catch (error) {
    elements["run-error"].textContent = error.message;
  }
}

async function pollRun() {
  if (!state.run) return;
  try {
    state.run = await api(`/api/ai-runs/${state.run.id}`);
    renderRun();
    if (["completed", "failed", "cancelled"].includes(state.run.status)) {
      clearInterval(state.runTimer);
      state.runTimer = null;
      await refreshHistory();
    }
  } catch (error) {
    clearInterval(state.runTimer);
    elements["run-error"].textContent = error.message;
  }
}

function renderRun() {
  const run = state.run;
  if (!run) return;
  const stats = run.statistics;
  const percent = run.requestedGames ? (run.completedGames / run.requestedGames) * 100 : 0;
  elements["run-status"].textContent = `${run.status} · ${run.completedGames}/${run.requestedGames}`;
  elements["run-progress"].style.width = `${percent}%`;
  elements["stat-games"].textContent = stats.games;
  elements["stat-player1"].textContent = stats.player1Wins;
  elements["stat-player2"].textContent = stats.player2Wins;
  elements["stat-draws"].textContent = stats.draws;
  elements["stat-moves"].textContent = stats.totalMoves;
  elements["stat-rate"].textContent = stats.gamesPerSecond.toFixed(2);
  elements["run-error"].textContent = run.error ?? "";
}

async function refreshHistory() {
  state.history = await api("/api/games?limit=100");
  renderHistoryList();
  if (!state.selectedGame && state.history.length) await selectHistory(state.history[0].id);
}

function renderHistoryList() {
  if (!state.history.length) {
    elements["history-list"].innerHTML = '<div class="history-item"><strong>No completed games</strong><span>Run the arena or finish a human game</span></div>';
    return;
  }
  const fragment = document.createDocumentFragment();
  state.history.forEach(game => {
    const button = document.createElement("button");
    button.type = "button";
    button.className = "history-item";
    button.classList.toggle("is-selected", state.selectedGame?.id === game.id);
    button.setAttribute("role", "option");
    button.setAttribute("aria-selected", state.selectedGame?.id === game.id ? "true" : "false");
    button.innerHTML = `<strong>${escapeHtml(game.player1Id)} vs ${escapeHtml(game.player2Id)}</strong>` +
      `<span>${new Date(game.completedAt).toLocaleString()} · ${game.moves.length} moves</span>` +
      `<span class="outcome">${escapeHtml(game.outcome)} · ${escapeHtml(game.openingId ?? "standard")}</span>`;
    button.addEventListener("click", () => selectHistory(game.id));
    fragment.appendChild(button);
  });
  elements["history-list"].replaceChildren(fragment);
}

async function selectHistory(id) {
  state.selectedGame = await api(`/api/games/${id}`);
  state.replayIndex = state.selectedGame.moves.length;
  elements["replay-slider"].max = state.selectedGame.moves.length;
  elements["replay-slider"].value = state.replayIndex;
  renderHistoryList();
  renderReplay();
}

function renderReplay() {
  const game = state.selectedGame;
  if (!game) {
    renderBoard(elements["replay-board"], [], false, null);
    return;
  }
  renderBoard(elements["replay-board"], game.moves, false, null, game, state.replayIndex);
  elements["replay-step"].textContent = `${state.replayIndex} / ${game.moves.length}`;
  elements["replay-players"].textContent = `${game.player1Id} vs ${game.player2Id} · ${game.openingId ?? "standard"}`;
  const times = calculateThinkTimes(game.moves, state.replayIndex);
  elements["replay-x-time"].textContent = formatThinkTime(times.X);
  elements["replay-o-time"].textContent = formatThinkTime(times.O);
  elements["replay-status"].textContent = state.replayIndex === game.moves.length ? game.status : `Move ${state.replayIndex}`;
  elements["replay-slider"].value = state.replayIndex;
}

function handleReplayKeys(event) {
  if (state.activeView !== "archive" || !state.selectedGame) return;
  if (["INPUT", "SELECT", "TEXTAREA"].includes(event.target.tagName)) return;
  const last = state.selectedGame.moves.length;
  const changes = {
    ArrowLeft: -1,
    ArrowUp: -1,
    ArrowRight: 1,
    ArrowDown: 1,
    PageUp: -10,
    PageDown: 10
  };
  if (event.key in changes) state.replayIndex = clamp(state.replayIndex + changes[event.key], 0, last);
  else if (event.key === "Home") state.replayIndex = 0;
  else if (event.key === "End") state.replayIndex = last;
  else return;
  event.preventDefault();
  renderReplay();
}

function statusText(game) {
  if (game.status === "InProgress") return game.nextMark === game.humanMark ? "Your turn" : "AI turn";
  if (game.status === "Draw") return "Draw";
  const winner = game.status.startsWith("X") ? "X" : "O";
  return winner === game.humanMark ? "You won" : "AI won";
}

function syncHumanClock(game) {
  const times = calculateThinkTimes(game.moves);
  state.humanClock = {
    ...times,
    activeMark: game.status === "InProgress" && game.moves.length > 0 ? game.nextMark : null,
    startedAt: performance.now()
  };
  renderHumanTimes();
}

function beginHumanClockTurn(mark) {
  if (!state.humanClock) state.humanClock = { X: 0, O: 0, activeMark: null, startedAt: performance.now() };
  if (state.humanClock.activeMark) {
    state.humanClock[state.humanClock.activeMark] += performance.now() - state.humanClock.startedAt;
  }
  state.humanClock.activeMark = mark;
  state.humanClock.startedAt = performance.now();
}

function renderHumanTimes() {
  const clock = state.humanClock;
  let xMilliseconds = clock?.X ?? 0;
  let oMilliseconds = clock?.O ?? 0;
  if (clock?.activeMark) {
    const activeMilliseconds = performance.now() - clock.startedAt;
    if (clock.activeMark === "X") xMilliseconds += activeMilliseconds;
    else oMilliseconds += activeMilliseconds;
  }
  elements["human-x-time"].textContent = formatThinkTime(xMilliseconds);
  elements["human-o-time"].textContent = formatThinkTime(oMilliseconds);
}

function calculateThinkTimes(moves, throughMove = moves.length) {
  return moves.slice(0, throughMove).reduce((times, move) => {
    times[move.mark] += Number(move.elapsedMilliseconds) || 0;
    return times;
  }, { X: 0, O: 0 });
}

function formatThinkTime(milliseconds) {
  const totalSeconds = Math.max(0, milliseconds) / 1000;
  if (totalSeconds < 60) return `${totalSeconds.toFixed(1)}s`;
  const minutes = Math.floor(totalSeconds / 60);
  return `${minutes}:${(totalSeconds % 60).toFixed(1).padStart(4, "0")}`;
}

function setBusy(value) {
  state.busy = value;
  elements["new-human-game"].disabled = value;
  if (state.humanGame) renderHumanGame();
}

async function api(path, options = {}) {
  const response = await fetch(path, {
    headers: { "Content-Type": "application/json", ...(options.headers ?? {}) },
    ...options
  });
  if (!response.ok) {
    const body = await response.json().catch(() => ({}));
    throw new Error(body.detail ?? body.error ?? `Request failed (${response.status})`);
  }
  return response.status === 204 ? null : response.json();
}

function clamp(value, min, max) { return Math.min(max, Math.max(min, value)); }

function escapeHtml(value) {
  return String(value).replace(/[&<>'"]/g, character => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;"
  })[character]);
}