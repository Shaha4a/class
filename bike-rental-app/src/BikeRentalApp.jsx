import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';

const API_BASE = process.env.REACT_APP_API_BASE || 'http://localhost:4011';
const HUB_URL = `${API_BASE}/hubs/class`;
const STORAGE_KEY = 'classin_mvp_auth';
const ROLE_CLAIM_URI = 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role';

const defaultHeaders = (token) => ({
  'Content-Type': 'application/json',
  ...(token ? { Authorization: `Bearer ${token}` } : {})
});

async function apiRequest(path, options = {}, token = '') {
  let response;

  try {
    response = await fetch(`${API_BASE}${path}`, {
      ...options,
      headers: {
        ...defaultHeaders(token),
        ...(options.headers || {})
      }
    });
  } catch {
    throw new Error(`Cannot reach API at ${API_BASE}. Check backend run/cors/url.`);
  }

  const raw = await response.text();
  let data = {};

  try {
    data = raw ? JSON.parse(raw) : {};
  } catch {
    data = {};
  }

  if (!response.ok) {
    throw new Error(data?.error || data?.title || `Request failed (${response.status})`);
  }

  return data;
}

function tryLoadAuth() {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (!parsed?.token) return null;
    return {
      ...parsed,
      role: normalizeRole(parsed.role ?? extractRoleFromToken(parsed.token))
    };
  } catch {
    return null;
  }
}

function saveAuth(auth) {
  localStorage.setItem(STORAGE_KEY, JSON.stringify(auth));
}

function clearAuth() {
  localStorage.removeItem(STORAGE_KEY);
}

function uniqueAdd(list, value) {
  return list.includes(value) ? list : [...list, value];
}

function roleLabel(role) {
  return normalizeRole(role) === 1 ? 'Teacher' : 'Student';
}

function normalizeRole(role) {
  if (typeof role === 'string') {
    const value = role.trim().toLowerCase();
    if (value === 'teacher') return 1;
    if (value === 'student') return 2;
  }

  const numeric = Number(role);
  return numeric === 1 ? 1 : 2;
}

function decodeJwtPayload(token) {
  if (!token || typeof token !== 'string') return null;
  const parts = token.split('.');
  if (parts.length < 2) return null;

  try {
    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4);
    const json = atob(padded);
    return JSON.parse(json);
  } catch {
    return null;
  }
}

function extractRoleFromToken(token) {
  const payload = decodeJwtPayload(token);
  if (!payload || typeof payload !== 'object') return null;
  return payload.role ?? payload[ROLE_CLAIM_URI] ?? null;
}

export default function BikeRentalApp() {
  const [auth, setAuth] = useState(() => tryLoadAuth());
  const [isRegister, setIsRegister] = useState(false);
  const [authForm, setAuthForm] = useState({ name: '', email: '', password: '', role: 2 });
  const [authError, setAuthError] = useState('');

  const [classes, setClasses] = useState([]);
  const [selectedClassId, setSelectedClassId] = useState(null);
  const [createClassName, setCreateClassName] = useState('');
  const [joinClassId, setJoinClassId] = useState('');

  const [messages, setMessages] = useState([]);
  const [messageText, setMessageText] = useState('');
  const [onlineUsers, setOnlineUsers] = useState([]);
  const [statusText, setStatusText] = useState('Disconnected');
  const [globalError, setGlobalError] = useState('');
  const [activeView, setActiveView] = useState('classes');

  const [brushColor, setBrushColor] = useState('#ff6b35');
  const [brushWidth, setBrushWidth] = useState(3);
  const [tool, setTool] = useState('pen');

  const connectionRef = useRef(null);
  const canvasRef = useRef(null);
  const drawingRef = useRef(false);
  const lastPointRef = useRef({ x: 0, y: 0 });
  const strokesRef = useRef([]);
  const redoRef = useRef([]);

  const token = auth?.token || '';
  const isTeacher = normalizeRole(auth?.role) === 1;

  const selectedClass = useMemo(
    () => classes.find((item) => item.id === selectedClassId) || null,
    [classes, selectedClassId]
  );
  const jitsiUrl = selectedClass
    ? (selectedClass.jitsiRoomUrl || `https://meet.jit.si/classroom-${selectedClass.id}`)
    : '';

  const drawLine = useCallback((line, color, width) => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    ctx.strokeStyle = color;
    ctx.lineWidth = width;
    ctx.lineCap = 'round';
    ctx.beginPath();
    ctx.moveTo(line.x1, line.y1);
    ctx.lineTo(line.x2, line.y2);
    ctx.stroke();
  }, []);

  const clearBoardCanvas = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
  }, []);

  const redrawStrokes = useCallback(() => {
    clearBoardCanvas();
    for (const stroke of strokesRef.current) {
      drawLine(stroke, stroke.color, Number(stroke.lineWidth || 2));
    }
  }, [clearBoardCanvas, drawLine]);

  const clearBoardLocal = useCallback(() => {
    strokesRef.current = [];
    redoRef.current = [];
    clearBoardCanvas();
  }, [clearBoardCanvas]);

  const setupCanvas = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const rect = canvas.getBoundingClientRect();
    const ratio = window.devicePixelRatio || 1;

    canvas.width = Math.floor(rect.width * ratio);
    canvas.height = Math.floor(rect.height * ratio);

    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    ctx.scale(ratio, ratio);
    ctx.lineCap = 'round';
  }, []);

  const loadClasses = useCallback(async () => {
    if (!token) return;
    const data = await apiRequest('/api/class/my', { method: 'GET' }, token);
    setClasses(Array.isArray(data) ? data : []);
  }, [token]);

  const loadMessages = useCallback(
    async (classId) => {
      if (!token || !classId) return;
      const data = await apiRequest(`/api/message/${classId}`, { method: 'GET' }, token);
      setMessages(Array.isArray(data) ? data : []);
    },
    [token]
  );

  const disconnectHub = useCallback(async () => {
    if (connectionRef.current) {
      try {
        await connectionRef.current.stop();
      } catch {
        // ignore
      }
      connectionRef.current = null;
    }
    setOnlineUsers([]);
    setStatusText('Disconnected');
  }, []);

  const connectHub = useCallback(
    async (classId) => {
      await disconnectHub();

      if (!window.signalR) {
        setGlobalError('SignalR client is not loaded.');
        return;
      }

      const connection = new window.signalR.HubConnectionBuilder()
        .withUrl(`${HUB_URL}?classId=${classId}&access_token=${token}`)
        .withAutomaticReconnect()
        .build();

      connection.on('ReceiveMessage', (message) => {
        setMessages((prev) => [...prev, message]);
      });

      connection.on('UserOnline', (userId) => {
        setOnlineUsers((prev) => uniqueAdd(prev, Number(userId)));
      });

      connection.on('UserOffline', (userId) => {
        setOnlineUsers((prev) => prev.filter((id) => id !== Number(userId)));
      });

      connection.on('Draw', (drawEvent) => {
        if (drawEvent.color === '__clear__') {
          strokesRef.current = [];
          redoRef.current = [];
          clearBoardCanvas();
          return;
        }
        strokesRef.current.push(drawEvent);
        drawLine(drawEvent, drawEvent.color || '#0d1117', Number(drawEvent.lineWidth || 2));
      });

      connection.onreconnecting(() => setStatusText('Reconnecting...'));
      connection.onreconnected(() => setStatusText('Connected'));
      connection.onclose(() => setStatusText('Disconnected'));

      await connection.start();
      setStatusText('Connected');
      connectionRef.current = connection;

      try {
        await connection.invoke('JoinClass', classId);
      } catch {
        // server also auto-joins by query classId
      }
    },
    [disconnectHub, drawLine, token, clearBoardCanvas]
  );

  useEffect(() => {
    setupCanvas();
    const onResize = () => {
      setupCanvas();
      redrawStrokes();
    };
    window.addEventListener('resize', onResize);
    return () => window.removeEventListener('resize', onResize);
  }, [redrawStrokes, setupCanvas]);

  useEffect(() => {
    if (!token) return;
    loadClasses().catch((err) => setGlobalError(err.message));
  }, [token, loadClasses]);

  useEffect(() => {
    if (!selectedClassId || !token) return;

    setGlobalError('');
    clearBoardLocal();
    loadMessages(selectedClassId).catch((err) => setGlobalError(err.message));
    connectHub(selectedClassId).catch((err) => setGlobalError(err.message));

    return () => {
      disconnectHub().catch(() => {});
    };
  }, [selectedClassId, token, loadMessages, connectHub, disconnectHub, clearBoardLocal]);

  const handleAuthSubmit = async (event) => {
    event.preventDefault();
    setAuthError('');

    try {
      const endpoint = isRegister ? '/api/auth/register' : '/api/auth/login';
      const payload = isRegister
        ? {
            name: authForm.name,
            email: authForm.email,
            password: authForm.password,
            role: Number(authForm.role)
          }
        : {
            email: authForm.email,
            password: authForm.password
          };

      const data = await apiRequest(endpoint, { method: 'POST', body: JSON.stringify(payload) });
      const resolvedToken = data.token ?? data.Token;
      const resolvedRole = normalizeRole(data.role ?? data.Role ?? extractRoleFromToken(resolvedToken));
      const nextAuth = {
        token: resolvedToken,
        userId: data.userId ?? data.UserId,
        name: data.name ?? data.Name,
        email: data.email ?? data.Email,
        role: resolvedRole
      };

      saveAuth(nextAuth);
      setAuth(nextAuth);
      setAuthForm({ name: '', email: '', password: '', role: 2 });
    } catch (err) {
      setAuthError(err.message);
    }
  };

  const handleLogout = async () => {
    await disconnectHub();
    clearAuth();
    setAuth(null);
    setClasses([]);
    setSelectedClassId(null);
    setMessages([]);
  };

  const handleCreateClass = async () => {
    if (!createClassName.trim()) return;
    setGlobalError('');
    try {
      await apiRequest('/api/class', {
        method: 'POST',
        body: JSON.stringify({ name: createClassName.trim() })
      }, token);
      setCreateClassName('');
      await loadClasses();
    } catch (err) {
      setGlobalError(err.message);
    }
  };

  const handleJoinClass = async () => {
    const parsed = Number(joinClassId);
    if (!parsed) return;
    setGlobalError('');
    try {
      await apiRequest('/api/class/join', {
        method: 'POST',
        body: JSON.stringify({ classId: parsed })
      }, token);
      setJoinClassId('');
      await loadClasses();
      setSelectedClassId(parsed);
    } catch (err) {
      setGlobalError(err.message);
    }
  };

  const handleSendMessage = async () => {
    if (!messageText.trim() || !selectedClassId || !connectionRef.current) return;

    try {
      await connectionRef.current.invoke('SendMessage', selectedClassId, messageText.trim());
      setMessageText('');
    } catch (err) {
      setGlobalError(err.message || 'Cannot send message');
    }
  };

  const getCanvasPoint = (event) => {
    const rect = canvasRef.current.getBoundingClientRect();
    return {
      x: event.clientX - rect.left,
      y: event.clientY - rect.top
    };
  };

  const onPointerDown = (event) => {
    if (!selectedClassId) return;
    drawingRef.current = true;
    lastPointRef.current = getCanvasPoint(event);
  };

  const onPointerMove = async (event) => {
    if (!drawingRef.current || !selectedClassId) return;

    const next = getCanvasPoint(event);
    const previous = lastPointRef.current;
    const effectiveColor = tool === 'eraser' ? '#ffffff' : brushColor;
    const effectiveWidth = tool === 'eraser' ? Number(brushWidth) * 4 : Number(brushWidth);

    const payload = {
      classId: selectedClassId,
      x1: previous.x,
      y1: previous.y,
      x2: next.x,
      y2: next.y,
      color: effectiveColor,
      lineWidth: effectiveWidth
    };

    strokesRef.current.push(payload);
    redoRef.current = [];
    drawLine(payload, payload.color, payload.lineWidth);

    if (connectionRef.current) {
      try {
        await connectionRef.current.invoke('Draw', payload);
      } catch {
        // ignore draw failures
      }
    }

    lastPointRef.current = next;
  };

  const onPointerUp = () => {
    drawingRef.current = false;
  };

  const handleUndo = () => {
    if (!strokesRef.current.length) return;
    const last = strokesRef.current.pop();
    if (last) {
      redoRef.current.push(last);
      redrawStrokes();
    }
  };

  const handleRedo = () => {
    if (!redoRef.current.length) return;
    const next = redoRef.current.pop();
    if (next) {
      strokesRef.current.push(next);
      redrawStrokes();
    }
  };

  const handleClearForEveryone = async () => {
    clearBoardLocal();
    if (!selectedClassId || !connectionRef.current) return;
    try {
      await connectionRef.current.invoke('Draw', {
        classId: selectedClassId,
        x1: 0,
        y1: 0,
        x2: 0,
        y2: 0,
        color: '__clear__',
        lineWidth: 1
      });
    } catch (err) {
      setGlobalError(err.message || 'Cannot clear board for everyone');
    }
  };

  const handleExportPng = () => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const link = document.createElement('a');
    link.download = `board-class-${selectedClassId || 'unknown'}.png`;
    link.href = canvas.toDataURL('image/png');
    link.click();
  };

  const ensureClassSelected = () => {
    if (selectedClassId) {
      return true;
    }
    setGlobalError('Select class first.');
    return false;
  };

  if (!auth) {
    return (
      <div className="app auth-screen">
        <div className="card auth-card">
          <h1>Classroom MVP</h1>
          <p className="muted">Login or create account (Teacher / Student)</p>

          <form onSubmit={handleAuthSubmit} className="form-grid">
            {isRegister && (
              <label>
                Name
                <input
                  value={authForm.name}
                  onChange={(e) => setAuthForm((prev) => ({ ...prev, name: e.target.value }))}
                  required
                />
              </label>
            )}

            <label>
              Email
              <input
                type="email"
                value={authForm.email}
                onChange={(e) => setAuthForm((prev) => ({ ...prev, email: e.target.value }))}
                required
              />
            </label>

            <label>
              Password
              <input
                type="password"
                value={authForm.password}
                onChange={(e) => setAuthForm((prev) => ({ ...prev, password: e.target.value }))}
                required
              />
            </label>

            {isRegister && (
              <label>
                Role
                <select
                  value={authForm.role}
                  onChange={(e) => setAuthForm((prev) => ({ ...prev, role: Number(e.target.value) }))}
                >
                  <option value={2}>Student</option>
                  <option value={1}>Teacher</option>
                </select>
              </label>
            )}

            {authError && <div className="error-box">{authError}</div>}

            <button type="submit" className="primary-btn">
              {isRegister ? 'Register' : 'Login'}
            </button>
          </form>

          <button type="button" className="link-btn" onClick={() => setIsRegister((v) => !v)}>
            {isRegister ? 'Already have account? Login' : 'No account? Register'}
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="app dashboard">
      <header className="topbar">
        <div>
          <h1>Classroom MVP</h1>
          <p className="muted">
            {auth.name} ({roleLabel(auth.role)})
          </p>
        </div>
        <button className="secondary-btn" onClick={handleLogout}>
          Logout
        </button>
      </header>

      {globalError && <div className="error-box">{globalError}</div>}

      <main className="layout">
        <section className="card sidebar">
          <h2>Your classes</h2>
          <div className="status-row">
            <span>Status</span>
            <strong>{statusText}</strong>
          </div>

          {isTeacher && (
            <div className="stack">
              <input
                placeholder="New class name"
                value={createClassName}
                onChange={(e) => setCreateClassName(e.target.value)}
              />
              <button className="primary-btn" onClick={handleCreateClass}>
                Create class
              </button>
            </div>
          )}

          <div className="stack">
            <input
              placeholder="Join class ID"
              value={joinClassId}
              onChange={(e) => setJoinClassId(e.target.value)}
            />
            <button className="secondary-btn" onClick={handleJoinClass}>
              Join class
            </button>
          </div>

          <div className="class-list">
            {classes.map((item) => (
              <button
                key={item.id}
                className={`class-item ${selectedClassId === item.id ? 'active' : ''}`}
                onClick={() => setSelectedClassId(item.id)}
              >
                <span>{item.name}</span>
                <small>ID: {item.id}</small>
              </button>
            ))}
          </div>
        </section>

        <section className="content-grid">
          <div className="card view-nav">
            <button className={`view-btn ${activeView === 'classes' ? 'active' : ''}`} onClick={() => setActiveView('classes')}>
              Classes
            </button>
            <button className={`view-btn ${activeView === 'chat' ? 'active' : ''}`} onClick={() => setActiveView('chat')}>
              Chat
            </button>
            <button className={`view-btn ${activeView === 'video' ? 'active' : ''}`} onClick={() => setActiveView('video')}>
              Video
            </button>
            <button className={`view-btn ${activeView === 'board' ? 'active' : ''}`} onClick={() => setActiveView('board')}>
              Whiteboard
            </button>
          </div>

          {activeView === 'classes' && (
            <div className="card panel-card">
              <h2>Classroom</h2>
              {selectedClass ? (
                <>
                  <p className="muted">Selected class: {selectedClass.name} (ID: {selectedClass.id})</p>
                  <p className="muted">Members online: {onlineUsers.length}</p>
                </>
              ) : (
                <p className="muted">Pick a class from the left panel.</p>
              )}
            </div>
          )}

          {activeView === 'chat' && (
            <div className="card panel-card">
              <h2>Live chat</h2>
              <p className="muted">Online users: {onlineUsers.length ? onlineUsers.join(', ') : 'none'}</p>

              <div className="chat-messages">
                {messages.map((msg) => (
                  <div key={`${msg.id}-${msg.sentAt}`} className="message-item">
                    <strong>{msg.userName || `User ${msg.userId}`}</strong>
                    <span>{msg.text}</span>
                  </div>
                ))}
              </div>

              <div className="chat-input">
                <input
                  placeholder="Type message"
                  value={messageText}
                  onChange={(e) => setMessageText(e.target.value)}
                  disabled={!selectedClassId}
                />
                <button
                  className="primary-btn"
                  onClick={() => ensureClassSelected() && handleSendMessage()}
                  disabled={!selectedClassId}
                >
                  Send
                </button>
              </div>
            </div>
          )}

          {activeView === 'video' && (
            <div className="card panel-card">
              <h2>Video room</h2>
              {selectedClass ? (
                <>
                  <div className="stack" style={{ marginBottom: 10 }}>
                    <a href={jitsiUrl} target="_blank" rel="noreferrer" className="secondary-btn">
                      Open room in new tab
                    </a>
                  </div>
                  <iframe
                    title="jitsi"
                    src={jitsiUrl}
                    allow="camera; microphone; fullscreen; display-capture"
                  />
                </>
              ) : (
                <p className="muted">Select a class to open video room.</p>
              )}
            </div>
          )}

          {activeView === 'board' && (
            <div className="card panel-card">
              {selectedClass ? (
                <div className="board-video-wrap">
                  <h2>Live class</h2>
                  <iframe
                    title="class-video"
                    src={jitsiUrl}
                    allow="camera; microphone; fullscreen; display-capture"
                  />
                </div>
              ) : (
                <p className="muted">Select a class to start camera + board.</p>
              )}

              <div className="board-head">
                <h2>Whiteboard</h2>
                <div className="board-controls">
                  <input type="color" value={brushColor} onChange={(e) => setBrushColor(e.target.value)} />
                  <button
                    className={`secondary-btn ${tool === 'pen' ? 'active-tool' : ''}`}
                    onClick={() => setTool('pen')}
                  >
                    Pen
                  </button>
                  <button
                    className={`secondary-btn ${tool === 'eraser' ? 'active-tool' : ''}`}
                    onClick={() => setTool('eraser')}
                  >
                    Eraser
                  </button>
                  <input
                    type="range"
                    min="1"
                    max="10"
                    value={brushWidth}
                    onChange={(e) => setBrushWidth(Number(e.target.value))}
                  />
                  <button className="secondary-btn" onClick={handleUndo}>
                    Undo
                  </button>
                  <button className="secondary-btn" onClick={handleRedo}>
                    Redo
                  </button>
                  <button className="secondary-btn" onClick={clearBoardLocal}>
                    Clear local
                  </button>
                  <button className="secondary-btn" onClick={handleClearForEveryone}>
                    Clear all
                  </button>
                  <button className="secondary-btn" onClick={handleExportPng}>
                    Save PNG
                  </button>
                </div>
              </div>

              <canvas
                ref={canvasRef}
                className="board"
                onPointerDown={(e) => ensureClassSelected() && onPointerDown(e)}
                onPointerMove={onPointerMove}
                onPointerUp={onPointerUp}
                onPointerLeave={onPointerUp}
              />
            </div>
          )}
        </section>
      </main>
    </div>
  );
}
