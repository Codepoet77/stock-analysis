import { useEffect, useState, useCallback } from 'react';

const API = import.meta.env.VITE_API_URL || '';

export interface AuthUser {
  name: string;
  email: string;
  picture?: string;
}

export function useAuth() {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetch(`${API}/api/auth/me`, { credentials: 'include' })
      .then(r => r.ok ? r.json() : null)
      .then(data => {
        setUser(data ?? null);
        setLoading(false);
      })
      .catch(() => {
        setUser(null);
        setLoading(false);
      });
  }, []);

  const login = useCallback(() => {
    const returnUrl = encodeURIComponent(window.location.href);
    window.location.href = `${API}/api/auth/login?returnUrl=${returnUrl}`;
  }, []);

  const logout = useCallback(async () => {
    await fetch(`${API}/api/auth/logout`, { method: 'POST', credentials: 'include' });
    setUser(null);
  }, []);

  return { user, loading, login, logout };
}
