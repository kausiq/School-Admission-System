import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { api, serverNow } from './api.js';

const SID_RE = /^\d{17}$/;
const POLL_MS = 3000;
const PILL = {
  amber: 'bg-amber-100 text-amber-800',
  green: 'bg-emerald-100 text-emerald-800',
  red: 'bg-red-100 text-red-700',
  blue: 'bg-sky-100 text-sky-700',
  gray: 'bg-slate-100 text-slate-600',
};
const Pill = ({ c, children }) => (
  <span className={'inline-block rounded-full px-2.5 py-0.5 text-[13px] font-semibold ' + PILL[c]}>{children}</span>
);

const STATUS_BN = { HELD: 'অপেক্ষমাণ (Hold)', CONFIRMED: 'ভর্তি নিশ্চিত', EXPIRED: 'মেয়াদ শেষ', CANCELLED: 'বাতিল' };

const effStatus = (h, now) => (h.status === 'HELD' && new Date(h.expiresAt).getTime() <= now ? 'EXPIRED' : h.status);
const fmt = (ms) => {
  const s = Math.max(0, Math.floor(ms / 1000));
  return `${String(Math.floor(s / 60)).padStart(2, '0')}:${String(s % 60).padStart(2, '0')}`;
};

export default function App() {
  const [sid, setSid] = useState(() => localStorage.getItem('studentId') || '');
  const [tab, setTab] = useState(() => localStorage.getItem('tab') || 'classes');
  const [classes, setClasses] = useState(null);
  const [holds, setHolds] = useState([]);
  const [now, setNow] = useState(serverNow());
  const [netError, setNetError] = useState('');
  const [toast, setToast] = useState(null);
  const [busy, setBusy] = useState({});
  const validSid = SID_RE.test(sid);

  useEffect(() => { localStorage.setItem('studentId', sid); }, [sid]);
  useEffect(() => { localStorage.setItem('tab', tab); }, [tab]);
  useEffect(() => { const t = setInterval(() => setNow(serverNow()), 1000); return () => clearInterval(t); }, []);

  const notify = useCallback((type, text) => {
    setToast({ type, text, id: Math.random() });
  }, []);
  useEffect(() => {
    if (!toast) return;
    const t = setTimeout(() => setToast(null), 6000);
    return () => clearTimeout(t);
  }, [toast]);

  const refresh = useCallback(async () => {
    try {
      const [c, h] = await Promise.all([api.classes(), SID_RE.test(sid) ? api.myHolds(sid) : Promise.resolve([])]);
      setClasses(c); setHolds(h); setNetError('');
    } catch (e) { setNetError(e.message); }
  }, [sid]);

  useEffect(() => {
    refresh();
    const t = setInterval(refresh, POLL_MS);
    return () => clearInterval(t);
  }, [refresh]);

  const act = async (key, fn, okMsg) => {
    setBusy((b) => ({ ...b, [key]: true }));
    try { await fn(); if (okMsg) notify('ok', okMsg); }
    catch (e) { notify('err', e.message); }
    finally { setBusy((b) => ({ ...b, [key]: false })); refresh(); }
  };

  const needSid = () => { notify('err', 'আগে উপরের ঘরে শিক্ষার্থীর ১৭ অঙ্কের জন্ম নিবন্ধন নম্বর দিন।'); };

  const apply = (c) => (validSid ? act('apply' + c.id, () => api.apply(c.id, sid), `"${c.name}"-এ আবেদন সফল। মেয়াদ শেষ হওয়ার আগে ভর্তি ফি দিন।`) : needSid());
  const pay = (h) => act('pay' + h.holdId, () => api.confirm(h.holdId, sid), 'Payment সফল! ভর্তি নিশ্চিত হয়েছে।');
  const cancel = (h) => {
    if (!window.confirm('আবেদনটি বাতিল করতে চান?')) return;
    act('cancel' + h.holdId, () => api.cancel(h.holdId, sid), 'আবেদন বাতিল হয়েছে।');
  };

  const activeByClass = useMemo(() => {
    const m = {};
    holds.forEach((h) => { const s = effStatus(h, now); if (s === 'HELD' || s === 'CONFIRMED') m[h.classId] = s; });
    return m;
  }, [holds, now]);

  const active = holds.filter((h) => effStatus(h, now) === 'HELD').length;

  const tabs = [['classes', '📚', 'Class তালিকা'], ['mine', '📝', 'আমার আবেদন'], ['admin', '⚙️', 'Class তৈরি (Admin)']];
  const titles = { classes: 'Class তালিকা', mine: 'আমার আবেদন', admin: 'নতুন Class তৈরি' };

  return (
    <div className="flex min-h-screen flex-col bg-slate-50 text-slate-800 md:flex-row">
      <aside className="shrink-0 border-b border-slate-200 bg-white p-3 md:w-60 md:border-b-0 md:border-r">
        <div className="flex items-center gap-2.5 px-2.5 pb-4 pt-2 text-lg font-bold text-brand-500">
          <span className="rounded-lg bg-gradient-to-br from-brand-500 to-violet-400 px-2 py-1.5">🎓</span>
          School Admission
        </div>
        <div className="hidden px-2.5 pb-1 text-[11px] tracking-widest text-slate-400 md:block">MENU</div>
        <nav className="flex flex-wrap gap-1 md:flex-col">
          {tabs.map(([k, ic, l]) => (
            <button key={k} onClick={() => setTab(k)}
              className={'flex items-center gap-2.5 rounded-lg px-3 py-2.5 text-left text-[15px] transition ' +
                (tab === k ? 'bg-gradient-to-r from-brand-500 to-violet-400 text-white shadow-lg shadow-brand-500/30' : 'hover:bg-brand-50')}>
              <span>{ic}</span>{l}
              {k === 'mine' && active > 0 && <span className="ml-auto rounded-full bg-amber-500 px-2 text-xs text-white">{active}</span>}
            </button>
          ))}
        </nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex flex-wrap items-start justify-between gap-4 border-b border-slate-200 bg-white px-6 py-3.5">
          <h1 className="mt-1.5 text-xl font-semibold">{titles[tab]}</h1>
          <div className="flex w-full flex-col gap-1 md:w-80">
            <label htmlFor="sid" className="text-xs text-slate-500">শিক্ষার্থীর জন্ম নিবন্ধন নম্বর</label>
            <input id="sid" inputMode="numeric" maxLength={17} placeholder="১৭ অঙ্কের নম্বর (English digits)"
              value={sid} onChange={(e) => setSid(e.target.value.replace(/\D/g, ''))}
              className={'input ' + (sid && !validSid ? '!border-red-500' : validSid ? '!border-emerald-500' : '')} />
            {sid && !validSid && <small className="text-red-600">{sid.length}/১৭ — ঠিক ১৭টি অঙ্ক দিন</small>}
            {validSid && <small className="text-emerald-600">✓ ঠিক আছে</small>}
          </div>
        </header>

        {netError && (
          <div className="border-b border-red-200 bg-red-50 px-6 py-2.5 text-sm text-red-800">
            ⚠️ {netError} (স্বয়ংক্রিয়ভাবে আবার চেষ্টা করা হচ্ছে…)
          </div>
        )}

        <main className="flex-1 p-6">
          {tab === 'classes' && <ClassList classes={classes} activeByClass={activeByClass} busy={busy} onApply={apply} />}
          {tab === 'mine' && <MyHolds holds={holds} now={now} validSid={validSid} busy={busy} onPay={pay} onCancel={cancel} />}
          {tab === 'admin' && <CreateClass onCreated={(msg) => { notify('ok', msg); refresh(); }} onError={(m) => notify('err', m)} />}
        </main>
      </div>

      {toast && (
        <div role="alert" onClick={() => setToast(null)}
          className={'fixed bottom-5 right-5 z-10 max-w-sm cursor-pointer rounded-xl px-4 py-3.5 text-white shadow-2xl ' +
            (toast.type === 'ok' ? 'bg-emerald-500' : 'bg-red-500')}>
          {toast.text}
        </div>
      )}
    </div>
  );
}

const Muted = ({ children }) => <div className="card p-10 text-center text-slate-500">{children}</div>;

function ClassList({ classes, activeByClass, busy, onApply }) {
  if (!classes) return <Muted>লোড হচ্ছে…</Muted>;
  if (classes.length === 0) return <Muted>এখনো কোনো class তৈরি হয়নি। "Class তৈরি (Admin)" থেকে নতুন class যোগ করুন।</Muted>;
  const th = 'border-b border-slate-200 p-2.5 text-left text-xs font-semibold uppercase tracking-wide text-slate-500';
  return (
    <div className="card">
      <div className="overflow-x-auto">
        <table className="w-full border-collapse">
          <thead><tr>{['Class', 'মোট', 'Hold-এ', 'নিশ্চিত', 'খালি', 'Seat অবস্থা', ''].map((h, i) => <th key={i} className={th}>{h}</th>)}</tr></thead>
          <tbody>
            {classes.map((c) => {
              const mine = activeByClass[c.id];
              const full = c.availableSeats <= 0;
              const pct = c.totalSeats ? ((c.heldSeats + c.confirmedSeats) / c.totalSeats) * 100 : 0;
              const td = 'border-b border-slate-100 p-3.5';
              return (
                <tr key={c.id}>
                  <td className={td + ' font-semibold'}>{c.name}</td>
                  <td className={td}>{c.totalSeats}</td>
                  <td className={td}><Pill c="amber">{c.heldSeats}</Pill></td>
                  <td className={td}><Pill c="green">{c.confirmedSeats}</Pill></td>
                  <td className={td}><Pill c={full ? 'red' : 'blue'}>{Math.max(0, c.availableSeats)}</Pill></td>
                  <td className={td + ' min-w-[140px]'}>
                    <div className="h-2 overflow-hidden rounded-full bg-slate-100">
                      <div style={{ width: pct + '%' }} className={'h-full transition-all duration-500 ' + (full ? 'bg-red-500' : 'bg-gradient-to-r from-brand-500 to-violet-400')} />
                    </div>
                  </td>
                  <td className={td + ' text-right'}>
                    {mine === 'CONFIRMED' ? <Pill c="green">ভর্তি নিশ্চিত ✓</Pill>
                      : mine === 'HELD' ? <Pill c="amber">আবেদন করা হয়েছে</Pill>
                      : <button className="btn bg-brand-500 text-white hover:bg-brand-600" disabled={full || busy['apply' + c.id]} onClick={() => onApply(c)}>
                          {full ? 'Seat শেষ' : busy['apply' + c.id] ? 'অপেক্ষা করুন…' : 'আবেদন করুন'}
                        </button>}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <p className="mt-3 text-[13px] text-slate-500">Seat সংখ্যা প্রতি কয়েক সেকেন্ডে স্বয়ংক্রিয়ভাবে হালনাগাদ হয়।</p>
    </div>
  );
}

function MyHolds({ holds, now, validSid, busy, onPay, onCancel }) {
  if (!validSid) return <Muted>আপনার আবেদন দেখতে উপরের ঘরে শিক্ষার্থীর ১৭ অঙ্কের জন্ম নিবন্ধন নম্বর দিন।</Muted>;
  if (holds.length === 0) return <Muted>এই শিক্ষার্থীর কোনো আবেদন নেই। "Class তালিকা" থেকে আবেদন করুন।</Muted>;
  const color = { HELD: 'amber', CONFIRMED: 'green', EXPIRED: 'gray', CANCELLED: 'red' };
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
      {holds.map((h) => {
        const st = effStatus(h, now);
        const left = new Date(h.expiresAt).getTime() - now;
        return (
          <div className="card" key={h.holdId}>
            <div className="mb-3 flex items-center justify-between gap-2">
              <strong>{h.className}</strong>
              <Pill c={color[st]}>{STATUS_BN[st]}</Pill>
            </div>
            {st === 'HELD' && (
              <div className="mb-3 flex items-center justify-between rounded-lg bg-brand-50 px-3.5 py-2.5 text-slate-500">
                <span>মেয়াদ শেষ হতে বাকি</span>
                <b className={'text-2xl tabular-nums ' + (left < 60000 ? 'text-red-500' : 'text-brand-500')}>{fmt(left)}</b>
              </div>
            )}
            {st === 'EXPIRED' && <p className="text-[13px] text-slate-500">মেয়াদ শেষ হয়ে গেছে। Seat ছেড়ে দেওয়া হয়েছে — Class তালিকা থেকে আবার আবেদন করুন।</p>}
            {st === 'CONFIRMED' && <p className="text-[13px] text-emerald-600">ভর্তি নিশ্চিত হয়েছে। 🎉</p>}
            <div className="mt-2 flex gap-2.5">
              <button className="btn bg-emerald-500 text-white hover:bg-emerald-600" disabled={st !== 'HELD' || busy['pay' + h.holdId]} onClick={() => onPay(h)}>
                {busy['pay' + h.holdId] ? 'প্রক্রিয়াধীন…' : 'ভর্তি ফি দিন'}
              </button>
              <button className="btn border border-red-500 bg-white text-red-600 hover:bg-red-50" disabled={st !== 'HELD' || busy['cancel' + h.holdId]} onClick={() => onCancel(h)}>বাতিল</button>
            </div>
          </div>
        );
      })}
    </div>
  );
}

function CreateClass({ onCreated, onError }) {
  const [name, setName] = useState('');
  const [seats, setSeats] = useState('');
  const [saving, setSaving] = useState(false);
  const submit = async (e) => {
    e.preventDefault();
    const n = Number(seats);
    if (!name.trim()) return onError('Class-এর নাম দিন।');
    if (!Number.isInteger(n) || n < 1) return onError('Seat সংখ্যা ১ বা তার বেশি পূর্ণ সংখ্যা হতে হবে।');
    setSaving(true);
    try { await api.createClass(name.trim(), n); setName(''); setSeats(''); onCreated(`"${name.trim()}" তৈরি হয়েছে।`); }
    catch (err) { onError(err.message); }
    finally { setSaving(false); }
  };
  return (
    <form className="card flex max-w-md flex-col gap-3.5" onSubmit={submit}>
      <label className="flex flex-col gap-1.5 text-sm font-semibold">Class-এর নাম
        <input className="input font-normal" value={name} onChange={(e) => setName(e.target.value)} placeholder="যেমন: Class 6 - Morning Shift" maxLength={100} />
      </label>
      <label className="flex flex-col gap-1.5 text-sm font-semibold">মোট Seat
        <input className="input font-normal" type="number" min="1" value={seats} onChange={(e) => setSeats(e.target.value)} placeholder="যেমন: 120" />
      </label>
      <p className="text-[13px] text-slate-500">প্রতিটি shift আলাদা class হিসেবে তৈরি করুন (যেমন Morning ও Day shift আলাদা)।</p>
      <button className="btn bg-brand-500 text-white hover:bg-brand-600" disabled={saving}>{saving ? 'তৈরি হচ্ছে…' : 'Class তৈরি করুন'}</button>
    </form>
  );
}
