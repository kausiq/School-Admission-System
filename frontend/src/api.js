const BASE = (window.__CONFIG__?.API_URL || import.meta.env.VITE_API_URL || 'http://localhost:8080').replace(/\/$/, '');

let clockOffset = 0; // server time - local time, so countdowns match the server
export const serverNow = () => Date.now() + clockOffset;

export class ApiError extends Error {
  constructor(message, status) { super(message); this.status = status; }
}

const FALLBACK = {
  400: 'ভুল তথ্য দেওয়া হয়েছে। অনুগ্রহ করে যাচাই করে আবার চেষ্টা করুন।',
  403: 'এই কাজটি করার অনুমতি আপনার নেই।',
  404: 'যা খুঁজছেন তা পাওয়া যায়নি।',
  409: 'এই মুহূর্তে কাজটি করা সম্ভব হচ্ছে না।',
};

async function request(method, path, { sid, body } = {}) {
  const headers = {};
  if (sid) headers['X-Student-Id'] = sid;
  if (body) headers['Content-Type'] = 'application/json';
  let res;
  try {
    res = await fetch(BASE + path, { method, headers, body: body ? JSON.stringify(body) : undefined });
  } catch {
    throw new ApiError('সার্ভারের সাথে সংযোগ করা যাচ্ছে না। সার্ভার চালু আছে কি না দেখে আবার চেষ্টা করুন।', 0);
  }
  const d = res.headers.get('Date');
  if (d) clockOffset = new Date(d).getTime() - Date.now();
  if (res.status === 204) return null;
  let data = null;
  try { data = await res.json(); } catch { /* ignore */ }
  if (!res.ok) {
    const msg = data?.error || FALLBACK[res.status] || 'সার্ভারে সমস্যা হয়েছে। একটু পরে আবার চেষ্টা করুন।';
    throw new ApiError(msg, res.status);
  }
  return data;
}

export const api = {
  classes: () => request('GET', '/classes'),
  createClass: (name, totalSeats) => request('POST', '/classes', { body: { name, totalSeats } }),
  apply: (classId, sid) => request('POST', `/classes/${classId}/holds`, { sid }),
  confirm: (holdId, sid) => request('POST', `/holds/${holdId}/confirm`, { sid }),
  cancel: (holdId, sid) => request('DELETE', `/holds/${holdId}`, { sid }),
  myHolds: (sid) => request('GET', '/me/holds', { sid }),
};
