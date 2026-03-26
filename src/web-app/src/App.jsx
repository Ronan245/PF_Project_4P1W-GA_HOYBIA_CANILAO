import { createContext, useContext, useEffect, useState } from 'react';
import { Link, Navigate, Route, Routes, useLocation, useNavigate, useParams } from 'react-router-dom';

const authApi = import.meta.env.VITE_AUTH_API_URL ?? 'http://localhost:5001';
const resourceApi = import.meta.env.VITE_RESOURCE_API_URL ?? 'http://localhost:5002';

const AuthContext = createContext(null);

export default function App() {
  const [session, setSession] = useState(() => {
    const raw = localStorage.getItem('pf-project-session');
    return raw ? JSON.parse(raw) : null;
  });

  useEffect(() => {
    if (session) {
      localStorage.setItem('pf-project-session', JSON.stringify(session));
    } else {
      localStorage.removeItem('pf-project-session');
    }
  }, [session]);

  const auth = {
    session,
    user: session?.user ?? null,
    token: session?.token ?? null,
    setSession,
    logout: () => setSession(null)
  };

  return (
    <AuthContext.Provider value={auth}>
      <div className="app-shell">
        <TopBar />
        <main className="page-shell">
          <Routes>
            <Route path="/" element={<Navigate to="/packs" replace />} />
            <Route path="/login" element={<AuthPage mode="login" />} />
            <Route path="/register" element={<AuthPage mode="register" />} />
            <Route path="/packs" element={<ProtectedRoute><PacksPage /></ProtectedRoute>} />
            <Route path="/play/:packId" element={<ProtectedRoute><PlayPage /></ProtectedRoute>} />
            <Route path="/profile" element={<ProtectedRoute><ProfilePage /></ProtectedRoute>} />
            <Route path="/admin/images" element={<AdminRoute><AdminImagesPage /></AdminRoute>} />
            <Route path="/admin/tags" element={<AdminRoute><AdminTagsPage /></AdminRoute>} />
            <Route path="/admin/puzzles" element={<AdminRoute><AdminPuzzlesPage /></AdminRoute>} />
            <Route path="/admin/packs" element={<AdminRoute><AdminPacksPage /></AdminRoute>} />
          </Routes>
        </main>
      </div>
    </AuthContext.Provider>
  );
}

function TopBar() {
  const { user, logout } = useAuth();

  return (
    <header className="topbar">
      <div className="brand-lockup">
        <p className="eyebrow">PF_Project_4P1W</p>
        <h1>4 Pics 1 Word</h1>
        <p className="topbar-subtitle">Randomized packs, fast guesses, admin-built puzzle sets.</p>
      </div>
      <nav className="nav">
        {user ? (
          <>
            <Link to="/packs">Packs</Link>
            <Link to="/profile">Profile</Link>
            {user.role === 'admin' && (
              <>
                <Link to="/admin/images">Images</Link>
                <Link to="/admin/tags">Tags</Link>
                <Link to="/admin/puzzles">Puzzles</Link>
                <Link to="/admin/packs">Packs</Link>
              </>
            )}
            <button className="ghost" onClick={logout}>Logout</button>
          </>
        ) : (
          <>
            <Link to="/login">Login</Link>
            <Link to="/register">Register</Link>
          </>
        )}
      </nav>
    </header>
  );
}

function ProtectedRoute({ children }) {
  const { user } = useAuth();
  const location = useLocation();
  return user ? children : <Navigate to="/login" state={{ from: location.pathname }} replace />;
}

function AdminRoute({ children }) {
  const { user } = useAuth();
  const location = useLocation();
  if (!user) {
    return <Navigate to="/login" state={{ from: location.pathname }} replace />;
  }

  return user.role === 'admin' ? children : <Navigate to="/packs" replace />;
}

function AuthPage({ mode }) {
  const navigate = useNavigate();
  const location = useLocation();
  const { setSession } = useAuth();
  const [form, setForm] = useState({ email: '', password: '', displayName: '' });
  const [error, setError] = useState('');
  const [busy, setBusy] = useState(false);

  async function submit(event) {
    event.preventDefault();
    setBusy(true);
    setError('');

    try {
      const response = await fetch(`${authApi}/auth/${mode}`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(form)
      });
      const data = await response.json();

      if (!response.ok) {
        throw new Error(data.error ?? 'Request failed.');
      }

      setSession(data);
      navigate(location.state?.from ?? '/packs', { replace: true });
    } catch (requestError) {
      setError(requestError.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="auth-layout">
      <div className="hero-card auth-hero">
        <p className="eyebrow">Picture clue challenge</p>
        <h2>{mode === 'login' ? 'Four clues. One word.' : 'Build your player profile.'}</h2>
        <p className="hero-copy">
          Jump into a classic 4-picture guessing flow with shuffled packs and an admin-managed puzzle library.
        </p>
        <div className="hero-puzzle-preview" aria-hidden="true">
          <div className="mini-pic mini-pic-a"></div>
          <div className="mini-pic mini-pic-b"></div>
          <div className="mini-pic mini-pic-c"></div>
          <div className="mini-pic mini-pic-d"></div>
        </div>
      </div>
      <form className="panel auth-panel" onSubmit={submit}>
        <div className="auth-panel-header">
          <h3>{mode === 'login' ? 'Login' : 'Register'}</h3>
          <span className="badge accent-badge">{mode === 'login' ? 'Player Access' : 'New Account'}</span>
        </div>
        {mode === 'register' && (
          <label>
            Display name
            <input value={form.displayName} onChange={(event) => setForm({ ...form, displayName: event.target.value })} required />
          </label>
        )}
        <label>
          Email
          <input type="email" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} required />
        </label>
        <label>
          Password
          <input type="password" value={form.password} onChange={(event) => setForm({ ...form, password: event.target.value })} required />
        </label>
        {error && <p className="error">{error}</p>}
        <button className="primary" disabled={busy}>{busy ? 'Working...' : mode === 'login' ? 'Login' : 'Register'}</button>
      </form>
    </section>
  );
}

function PacksPage() {
  const { token } = useAuth();
  const [packs, setPacks] = useState([]);
  const [error, setError] = useState('');

  useEffect(() => {
    fetchJson(`${resourceApi}/packs?random=true`, { headers: authHeader(token) })
      .then(setPacks)
      .catch((requestError) => setError(requestError.message));
  }, [token]);

  return (
    <section className="stack">
      <section className="hero-strip">
        <div>
          <p className="eyebrow">Player</p>
          <h2>Published packs</h2>
          <p className="hero-copy">Pick a shuffled pack and start solving four-image word clues in a continuous loop.</p>
        </div>
        <div className="hero-metrics">
          <div className="metric-card">
            <span>Available packs</span>
            <strong>{packs.length}</strong>
          </div>
          <div className="metric-card">
            <span>Mode</span>
            <strong>Randomized</strong>
          </div>
        </div>
      </section>
      {error && <p className="error">{error}</p>}
      <div className="card-grid">
        {packs.map((pack) => (
          <article className="panel pack-card" key={pack.id}>
            <div className="pack-card-top">
              <span className="badge">{pack.puzzleCount} puzzles</span>
              <span className="badge subtle-badge">{pack.status}</span>
            </div>
            <h3>{pack.name}</h3>
            <p className="pack-description">{pack.description}</p>
            <Link className="primary button-link" to={`/play/${pack.id}`}>Play Pack</Link>
          </article>
        ))}
      </div>
    </section>
  );
}

function PlayPage() {
  const { packId } = useParams();
  const { token } = useAuth();
  const [screen, setScreen] = useState({ loading: true, completed: false, puzzle: null, feedback: '', score: null, error: '' });
  const [guess, setGuess] = useState('');
  const wrongAnswer = screen.feedback.startsWith('Wrong');

  async function loadNextPuzzle() {
    setScreen((current) => ({ ...current, loading: true, feedback: '', error: '' }));
    try {
      const data = await fetchJson(`${resourceApi}/puzzles/next?packId=${packId}`, { headers: authHeader(token) });
      setScreen((current) => ({
        ...current,
        loading: false,
        completed: data.packCompleted,
        puzzle: data.puzzle,
        error: ''
      }));
    } catch (requestError) {
      setScreen((current) => ({ ...current, loading: false, error: requestError.message }));
    }
  }

  useEffect(() => {
    loadNextPuzzle();
  }, [packId, token]);

  async function submitGuess(event) {
    event.preventDefault();
    try {
      const data = await fetchJson(`${resourceApi}/game/submit`, {
        method: 'POST',
        headers: { ...authHeader(token), 'Content-Type': 'application/json' },
        body: JSON.stringify({ packId, puzzleId: screen.puzzle.id, guess })
      });

      setScreen((current) => ({
        ...current,
        feedback: data.correct ? `Correct. Score ${data.scoreDelta >= 0 ? '+' : ''}${data.scoreDelta}` : `Wrong. Score ${data.scoreDelta}`,
        score: data.score
      }));
      if (data.correct) {
        setGuess('');
      }
    } catch (requestError) {
      setScreen((current) => ({ ...current, error: requestError.message }));
    }
  }

  async function restartPack() {
    await fetchJson(`${resourceApi}/game/restart`, {
      method: 'POST',
      headers: { ...authHeader(token), 'Content-Type': 'application/json' },
      body: JSON.stringify({ packId })
    });
    loadNextPuzzle();
  }

  if (screen.loading) {
    return <section className="panel"><h2>Loading puzzle...</h2></section>;
  }

  if (screen.completed) {
    return (
      <section className="panel">
        <p className="eyebrow">Pack completed</p>
        <h2>You solved every available puzzle in this pack.</h2>
        <div className="actions">
          <button className="primary" onClick={restartPack}>Restart Pack</button>
          <Link className="ghost button-link" to="/packs">Choose Another Pack</Link>
        </div>
      </section>
    );
  }

  return (
    <section className="stack">
      <div className="section-heading play-heading">
        <div>
          <p className="eyebrow">Gameplay</p>
          <h2>Find the answer word</h2>
          <p className="section-copy">Use the four visual clues, type your guess, then jump to the next random puzzle.</p>
        </div>
        <div className="play-status">
          <span className="badge">Length: {screen.puzzle?.answerLength}</span>
          {screen.score !== null && <span className="badge accent-badge">Score: {screen.score}</span>}
        </div>
      </div>
      {screen.error && <p className="error">{screen.error}</p>}
      <section className="play-layout">
        <div className="play-board">
          <div className="image-grid">
            {screen.puzzle?.images.map((image) => (
              <div className="play-image-frame" key={image.id}>
                <img className="image-card" src={image.url} alt={image.title} />
              </div>
            ))}
          </div>
        </div>
        <form className="panel play-panel" onSubmit={submitGuess}>
          <div className="answer-slots" aria-hidden="true">
            {Array.from({ length: screen.puzzle?.answerLength ?? 0 }, (_, index) => {
              const value = guess.replace(/\s/g, '').toUpperCase()[index] ?? '';
              return <span className={`answer-slot${wrongAnswer ? ' answer-slot-wrong' : ''}${value ? ' answer-slot-filled' : ''}`} key={index}>{value}</span>;
            })}
          </div>
          <div className="hint-box">
            <span className="hint-label">Hint</span>
            <p>{screen.puzzle?.hint ?? 'No hint for this puzzle.'}</p>
          </div>
          <label>
            Your guess
            <input
              className={wrongAnswer ? 'wrong-input' : ''}
              value={guess}
              onChange={(event) => {
                setGuess(event.target.value);
                if (wrongAnswer) {
                  setScreen((current) => ({ ...current, feedback: '' }));
                }
              }}
              placeholder="Enter the answer"
            />
          </label>
        <div className="actions">
          <button className="primary">Submit</button>
          <button className="ghost" onClick={() => setGuess('')} type="button">Clear</button>
          <button className="ghost" type="button" onClick={loadNextPuzzle}>Next Random Puzzle</button>
        </div>
          {screen.feedback && (
            <p className={screen.feedback.startsWith('Correct') ? 'success feedback-banner' : 'error feedback-banner wrong-feedback'}>
              {screen.feedback.startsWith('Wrong') ? 'Wrong answer. Try again.' : screen.feedback}
            </p>
          )}
        </form>
      </section>
    </section>
  );
}

function ProfilePage() {
  const { token, user } = useAuth();
  const [progress, setProgress] = useState(null);

  useEffect(() => {
    fetchJson(`${resourceApi}/profile/progress`, { headers: authHeader(token) }).then(setProgress);
  }, [token]);

  if (!progress) {
    return <section className="panel"><h2>Loading profile...</h2></section>;
  }

  return (
    <section className="stack">
      <div className="panel profile-hero">
        <p className="eyebrow">{user.displayName}</p>
        <h2>Progress summary</h2>
        <p className="section-copy">Track solved puzzles, total attempts, and score growth across randomized packs.</p>
        <div className="stats-grid">
          <Stat label="Solved" value={progress.solved} />
          <Stat label="Attempts" value={progress.attempts} />
          <Stat label="Score" value={progress.score} />
        </div>
      </div>
      <div className="panel">
        <h3>Recent puzzles</h3>
        <div className="list">
          {progress.recentPuzzles.map((item) => (
            <div className="list-item" key={item.puzzleId}>
              <strong>{item.answer}</strong>
              <span>{new Date(item.solvedAtUtc).toLocaleString()}</span>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}

function AdminImagesPage() {
  const { token } = useAuth();
  const [images, setImages] = useState([]);
  const [tags, setTags] = useState([]);
  const [urlForm, setUrlForm] = useState({ title: '', url: '' });
  const [tagDrafts, setTagDrafts] = useState({});
  const [activeTag, setActiveTag] = useState('');

  async function load() {
    const [imageData, tagData] = await Promise.all([
      fetchJson(`${resourceApi}/cms/images${activeTag ? `?tag=${encodeURIComponent(activeTag)}` : ''}`, { headers: authHeader(token) }),
      fetchJson(`${resourceApi}/cms/tags`, { headers: authHeader(token) })
    ]);
    setImages(imageData);
    setTags(tagData);
  }

  useEffect(() => {
    load();
  }, [token, activeTag]);

  async function submitUrl(event) {
    event.preventDefault();
    await fetchJson(`${resourceApi}/cms/images`, {
      method: 'POST',
      headers: { ...authHeader(token), 'Content-Type': 'application/json' },
      body: JSON.stringify(urlForm)
    });
    setUrlForm({ title: '', url: '' });
    load();
  }

  async function uploadFiles(event) {
    const files = Array.from(event.target.files ?? []);
    if (files.length === 0) {
      return;
    }

    const formData = new FormData();
    files.forEach((file) => formData.append('files', file));
    await fetchJson(`${resourceApi}/cms/images`, {
      method: 'POST',
      headers: authHeader(token),
      body: formData
    });
    load();
  }

  async function addTag(imageId) {
    const tag = tagDrafts[imageId];
    if (!tag) {
      return;
    }

    await fetchJson(`${resourceApi}/cms/images/${imageId}/tags`, {
      method: 'POST',
      headers: { ...authHeader(token), 'Content-Type': 'application/json' },
      body: JSON.stringify({ tags: [tag] })
    });
    setTagDrafts((current) => ({ ...current, [imageId]: '' }));
    load();
  }

  async function deleteImage(imageId) {
    await fetchJson(`${resourceApi}/cms/images/${imageId}`, {
      method: 'DELETE',
      headers: authHeader(token)
    });
    load();
  }

  return (
    <section className="stack">
      <div className="section-heading">
        <div>
          <p className="eyebrow">Admin CMS</p>
          <h2>Images</h2>
          <p className="section-copy">Upload clue art, preview the library, and assign tags for puzzle assembly.</p>
        </div>
      </div>
      <div className="card-grid two-up">
        <form className="panel" onSubmit={submitUrl}>
          <h3>Add image by URL</h3>
          <label>
            Title
            <input value={urlForm.title} onChange={(event) => setUrlForm({ ...urlForm, title: event.target.value })} />
          </label>
          <label>
            URL
            <input value={urlForm.url} onChange={(event) => setUrlForm({ ...urlForm, url: event.target.value })} />
          </label>
          <button className="primary">Save URL</button>
        </form>
        <div className="panel">
          <h3>Upload files</h3>
          <input type="file" multiple accept="image/*" onChange={uploadFiles} />
          <label>
            Filter by tag
            <select value={activeTag} onChange={(event) => setActiveTag(event.target.value)}>
              <option value="">All tags</option>
              {tags.map((tag) => <option key={tag} value={tag}>{tag}</option>)}
            </select>
          </label>
        </div>
      </div>
      <div className="card-grid">
        {images.map((image) => (
          <article className="panel library-card" key={image.id}>
            <img className="image-card" src={image.url} alt={image.title} />
            <h3>{image.title}</h3>
            <div className="chip-row">
              {image.tags.map((tag) => <span className="chip" key={tag}>{tag}</span>)}
            </div>
            <div className="inline-form">
              <select value={tagDrafts[image.id] ?? ''} onChange={(event) => setTagDrafts((current) => ({ ...current, [image.id]: event.target.value }))}>
                <option value="">Select tag</option>
                {tags.map((tag) => <option key={tag} value={tag}>{tag}</option>)}
              </select>
              <button className="ghost" type="button" onClick={() => addTag(image.id)}>Add tag</button>
              <button className="ghost" type="button" onClick={() => deleteImage(image.id)}>Delete</button>
            </div>
          </article>
        ))}
      </div>
    </section>
  );
}

function AdminTagsPage() {
  const { token } = useAuth();
  const [tags, setTags] = useState([]);
  const [draft, setDraft] = useState('');

  async function load() {
    setTags(await fetchJson(`${resourceApi}/cms/tags`, { headers: authHeader(token) }));
  }

  useEffect(() => {
    load();
  }, [token]);

  async function createTag(event) {
    event.preventDefault();
    await fetchJson(`${resourceApi}/cms/tags`, {
      method: 'POST',
      headers: { ...authHeader(token), 'Content-Type': 'application/json' },
      body: JSON.stringify({ tag: draft })
    });
    setDraft('');
    load();
  }

  async function deleteTag(tag) {
    await fetchJson(`${resourceApi}/cms/tags/${encodeURIComponent(tag)}`, {
      method: 'DELETE',
      headers: authHeader(token)
    });
    load();
  }

  return (
    <section className="stack">
      <form className="panel" onSubmit={createTag}>
        <h2>Tag catalog</h2>
        <p className="section-copy">Use tags to filter images and assemble puzzle-ready groups quickly.</p>
        <div className="inline-form">
          <input value={draft} onChange={(event) => setDraft(event.target.value)} placeholder="animal, food, school..." />
          <button className="primary">Create tag</button>
        </div>
      </form>
      <div className="panel">
        <div className="chip-row">
          {tags.map((tag) => (
            <button key={tag} className="chip" onClick={() => deleteTag(tag)}>{tag}</button>
          ))}
        </div>
      </div>
    </section>
  );
}

function AdminPuzzlesPage() {
  const { token } = useAuth();
  const [puzzles, setPuzzles] = useState([]);
  const [images, setImages] = useState([]);
  const [packs, setPacks] = useState([]);
  const [error, setError] = useState('');
  const [form, setForm] = useState({ answer: '', hint: '', difficulty: 'easy', imageIds: [], acceptableVariants: '', packIds: [] });

  async function load() {
    const [puzzleData, imageData, packData] = await Promise.all([
      fetchJson(`${resourceApi}/cms/puzzles`, { headers: authHeader(token) }),
      fetchJson(`${resourceApi}/cms/images`, { headers: authHeader(token) }),
      fetchJson(`${resourceApi}/cms/packs`, { headers: authHeader(token) })
    ]);
    setPuzzles(puzzleData);
    setImages(imageData);
    setPacks(packData);
  }

  useEffect(() => {
    load();
  }, [token]);

  async function createPuzzle(event) {
    event.preventDefault();
    setError('');
    try {
      await fetchJson(`${resourceApi}/cms/puzzles`, {
        method: 'POST',
        headers: { ...authHeader(token), 'Content-Type': 'application/json' },
        body: JSON.stringify({
          ...form,
          acceptableVariants: form.acceptableVariants.split(',').map((value) => value.trim()).filter(Boolean)
        })
      });
      setForm({ answer: '', hint: '', difficulty: 'easy', imageIds: [], acceptableVariants: '', packIds: [] });
      load();
    } catch (requestError) {
      setError(requestError.message);
    }
  }

  async function deletePuzzle(puzzleId) {
    await fetchJson(`${resourceApi}/cms/puzzles/${puzzleId}`, {
      method: 'DELETE',
      headers: authHeader(token)
    });
    load();
  }

  return (
    <section className="stack">
      <form className="panel" onSubmit={createPuzzle}>
        <h2>Create puzzle</h2>
        <p className="section-copy">Choose exactly four images, define the answer, and assign the puzzle into packs.</p>
        {error && <p className="error">{error}</p>}
        <label>
          Answer
          <input value={form.answer} onChange={(event) => setForm({ ...form, answer: event.target.value })} />
        </label>
        <label>
          Hint
          <input value={form.hint} onChange={(event) => setForm({ ...form, hint: event.target.value })} />
        </label>
        <label>
          Difficulty
          <select value={form.difficulty} onChange={(event) => setForm({ ...form, difficulty: event.target.value })}>
            <option value="easy">Easy</option>
            <option value="medium">Medium</option>
            <option value="hard">Hard</option>
          </select>
        </label>
        <label>
          Acceptable variants
          <input value={form.acceptableVariants} onChange={(event) => setForm({ ...form, acceptableVariants: event.target.value })} placeholder="comma separated" />
        </label>
        <fieldset className="selector-grid">
          <legend>Select 4 images</legend>
          {images.map((image) => (
            <label className="selector-card" key={image.id}>
              <input
                type="checkbox"
                checked={form.imageIds.includes(image.id)}
                onChange={() => setForm((current) => ({
                  ...current,
                  imageIds: current.imageIds.includes(image.id)
                    ? current.imageIds.filter((id) => id !== image.id)
                    : [...current.imageIds, image.id].slice(-4)
                }))}
              />
              <img src={image.url} alt={image.title} />
              <span>{image.title}</span>
            </label>
          ))}
        </fieldset>
        <fieldset className="selector-grid">
          <legend>Assign packs</legend>
          {packs.map((pack) => (
            <label className="selector-card compact" key={pack.id}>
              <input
                type="checkbox"
                checked={form.packIds.includes(pack.id)}
                onChange={() => setForm((current) => ({
                  ...current,
                  packIds: current.packIds.includes(pack.id)
                    ? current.packIds.filter((id) => id !== pack.id)
                    : [...current.packIds, pack.id]
                }))}
              />
              <span>{pack.name}</span>
            </label>
          ))}
        </fieldset>
        <button className="primary">Save puzzle</button>
      </form>
      <div className="card-grid">
        {puzzles.map((puzzle) => (
          <article className="panel cms-card" key={puzzle.id}>
            <p className="eyebrow">{puzzle.difficulty}</p>
            <h3>{puzzle.answer}</h3>
            <p>{puzzle.hint}</p>
            <div className="chip-row">
              {puzzle.packIds.map((packId) => {
                const pack = packs.find((item) => item.id === packId);
                return <span className="chip" key={packId}>{pack?.name ?? packId}</span>;
              })}
            </div>
            <button className="ghost" type="button" onClick={() => deletePuzzle(puzzle.id)}>Delete</button>
          </article>
        ))}
      </div>
    </section>
  );
}

function AdminPacksPage() {
  const { token } = useAuth();
  const [packs, setPacks] = useState([]);
  const [puzzles, setPuzzles] = useState([]);
  const [error, setError] = useState('');
  const [form, setForm] = useState({ name: '', description: '', visibility: 'public', sortOrder: 1, puzzleIds: [] });

  async function load() {
    const [packData, puzzleData] = await Promise.all([
      fetchJson(`${resourceApi}/cms/packs`, { headers: authHeader(token) }),
      fetchJson(`${resourceApi}/cms/puzzles`, { headers: authHeader(token) })
    ]);
    setPacks(packData);
    setPuzzles(puzzleData);
  }

  useEffect(() => {
    load();
  }, [token]);

  async function createPack(event) {
    event.preventDefault();
    setError('');
    try {
      await fetchJson(`${resourceApi}/cms/packs`, {
        method: 'POST',
        headers: { ...authHeader(token), 'Content-Type': 'application/json' },
        body: JSON.stringify({ ...form, sortOrder: Number(form.sortOrder) })
      });
      setForm({ name: '', description: '', visibility: 'public', sortOrder: 1, puzzleIds: [] });
      load();
    } catch (requestError) {
      setError(requestError.message);
    }
  }

  async function togglePublish(pack) {
    await fetchJson(`${resourceApi}/cms/packs/${pack.id}/publish`, {
      method: 'POST',
      headers: { ...authHeader(token), 'Content-Type': 'application/json' },
      body: JSON.stringify({ published: pack.status !== 'published' })
    });
    load();
  }

  async function deletePack(packId) {
    await fetchJson(`${resourceApi}/cms/packs/${packId}`, {
      method: 'DELETE',
      headers: authHeader(token)
    });
    load();
  }

  return (
    <section className="stack">
      <form className="panel" onSubmit={createPack}>
        <h2>Create pack</h2>
        <p className="section-copy">Bundle puzzles into a playable pack, control order, then publish when ready.</p>
        {error && <p className="error">{error}</p>}
        <label>
          Name
          <input value={form.name} onChange={(event) => setForm({ ...form, name: event.target.value })} />
        </label>
        <label>
          Description
          <textarea value={form.description} onChange={(event) => setForm({ ...form, description: event.target.value })} />
        </label>
        <label>
          Sort order
          <input type="number" value={form.sortOrder} onChange={(event) => setForm({ ...form, sortOrder: event.target.value })} />
        </label>
        <fieldset className="selector-grid">
          <legend>Add puzzles</legend>
          {puzzles.map((puzzle) => (
            <label className="selector-card compact" key={puzzle.id}>
              <input
                type="checkbox"
                checked={form.puzzleIds.includes(puzzle.id)}
                onChange={() => setForm((current) => ({
                  ...current,
                  puzzleIds: current.puzzleIds.includes(puzzle.id)
                    ? current.puzzleIds.filter((id) => id !== puzzle.id)
                    : [...current.puzzleIds, puzzle.id]
                }))}
              />
              <span>{puzzle.answer}</span>
            </label>
          ))}
        </fieldset>
        <button className="primary">Save pack</button>
      </form>
      <div className="card-grid">
        {packs.map((pack) => (
          <article className="panel cms-card" key={pack.id}>
            <p className="eyebrow">{pack.status}</p>
            <h3>{pack.name}</h3>
            <p>{pack.description}</p>
            <div className="chip-row">
              {pack.puzzles.map((puzzle) => <span className="chip" key={puzzle.id}>{puzzle.answer}</span>)}
            </div>
            <button className="ghost" onClick={() => togglePublish(pack)}>
              {pack.status === 'published' ? 'Unpublish' : 'Publish'}
            </button>
            <button className="ghost" type="button" onClick={() => deletePack(pack.id)}>Delete</button>
          </article>
        ))}
      </div>
    </section>
  );
}

function Stat({ label, value }) {
  return (
    <div className="stat-card">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function useAuth() {
  return useContext(AuthContext);
}

function authHeader(token) {
  return token ? { Authorization: `Bearer ${token}` } : {};
}

async function fetchJson(url, options = {}) {
  const response = await fetch(url, options);
  if (response.status === 204) {
    return null;
  }

  const data = await response.json();
  if (!response.ok) {
    throw new Error(data.error ?? 'Request failed.');
  }

  return data;
}
