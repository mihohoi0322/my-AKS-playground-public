"use client";

import { useEffect, useRef, useState } from "react";
import { apiFetch } from "@/lib/api";

interface Organization {
  orgId: string;
  name: string;
  parentOrgId: string;
  managerId: string;
  description: string;
  level: number;
}

type ModalState =
  | { mode: "create" }
  | { mode: "edit"; item: Organization }
  | null;

interface OrganizationForm {
  name: string;
  parentOrgId: string;
  description: string;
}

const emptyForm: OrganizationForm = {
  name: "",
  parentOrgId: "",
  description: "",
};

export default function OrganizationsPage() {
  const [orgs, setOrgs] = useState<Organization[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [modal, setModal] = useState<ModalState>(null);
  const [form, setForm] = useState<OrganizationForm>(emptyForm);
  const [submitting, setSubmitting] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);

  const fetchOrgs = async () => {
    const data = await apiFetch<{ organizations: Organization[] }>("/api/organizations");
    return data.organizations ?? [];
  };

  useEffect(() => {
    fetchOrgs()
      .then(setOrgs)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : "Unknown error"))
      .finally(() => setLoading(false));
  }, []);

  const closeModal = () => {
    setModal(null);
    requestAnimationFrame(() => triggerRef.current?.focus());
  };

  useEffect(() => {
    if (!modal) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        closeModal();
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [modal]);

  const openCreate = () => {
    setForm(emptyForm);
    setModal({ mode: "create" });
  };

  const openEdit = (org: Organization) => {
    setForm({
      name: org.name,
      parentOrgId: org.parentOrgId,
      description: org.description,
    });
    setModal({ mode: "edit", item: org });
  };

  const reload = async () => {
    setLoading(true);
    try {
      const data = await fetchOrgs();
      setOrgs(data);
    } finally {
      setLoading(false);
    }
  };

  const handleSubmit = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!modal) return;
    setError(null);
    setSubmitting(true);
    try {
      if (modal.mode === "create") {
        await apiFetch("/api/organizations", {
          method: "POST",
          body: JSON.stringify({
            name: form.name,
            parentOrgId: form.parentOrgId,
            description: form.description,
          }),
        });
      } else {
        await apiFetch(`/api/organizations/${modal.item.orgId}`, {
          method: "PATCH",
          body: JSON.stringify({
            name: form.name,
            description: form.description,
          }),
        });
      }
      closeModal();
      await reload();
    } catch (err: unknown) {
      const fallback = modal.mode === "create" ? "作成に失敗しました" : "保存に失敗しました";
      setError(err instanceof Error ? err.message : fallback);
    } finally {
      setSubmitting(false);
    }
  };

  const isCreate = modal?.mode === "create";
  const modalTitle = isCreate ? "組織を追加" : "組織を編集";
  const submitLabel = isCreate ? "追加" : "保存";

  return (
    <div>
      <div className="flex items-center justify-between mb-2">
        <h2 className="text-xl font-semibold tracking-wide">組織構成</h2>
        <button
          ref={triggerRef}
          onClick={openCreate}
          className="bg-[var(--primary)] text-white px-4 py-2 rounded-xl text-sm hover:bg-[var(--primary-hover)] transition-colors"
        >
          + 追加
        </button>
      </div>
      <p className="text-sm text-[var(--muted)] mb-6">組織の階層構造を管理</p>

      {error && (
        <div className="bg-[var(--danger-light)] border border-[var(--danger)] text-[var(--danger)] rounded-2xl p-4 mb-4 text-sm">
          {error}
          <button onClick={() => setError(null)} className="ml-2 underline">閉じる</button>
        </div>
      )}

      {/* Modal (create / edit) */}
      {modal && (
        <div className="fixed inset-0 bg-black/30 flex items-center justify-center z-50" onClick={closeModal}>
          <div
            role="dialog"
            aria-modal="true"
            aria-labelledby="org-modal-title"
            className="bg-[var(--card)] border border-[var(--border)] rounded-2xl p-6 w-full max-w-md shadow-lg"
            onClick={(e) => e.stopPropagation()}
          >
            <h3 id="org-modal-title" className="text-lg font-semibold mb-4">{modalTitle}</h3>
            <form onSubmit={handleSubmit}>
              <div className="space-y-3">
                <div>
                  <label htmlFor="org-name" className="block text-xs uppercase tracking-wider text-[var(--muted)] mb-1">組織名</label>
                  <input
                    id="org-name"
                    type="text"
                    required
                    autoFocus
                    placeholder="営業部"
                    value={form.name}
                    onChange={(e) => setForm({ ...form, name: e.target.value })}
                    className="w-full px-3 py-2 rounded-xl border border-[var(--border)] bg-[var(--background)] text-sm focus:outline-none focus:border-[var(--primary)]"
                  />
                </div>
                {isCreate && (
                  <div>
                    <label htmlFor="org-parentOrgId" className="block text-xs uppercase tracking-wider text-[var(--muted)] mb-1">親組織 ID</label>
                    <input
                      id="org-parentOrgId"
                      type="text"
                      placeholder="（任意）親組織ID"
                      value={form.parentOrgId}
                      onChange={(e) => setForm({ ...form, parentOrgId: e.target.value })}
                      className="w-full px-3 py-2 rounded-xl border border-[var(--border)] bg-[var(--background)] text-sm focus:outline-none focus:border-[var(--primary)]"
                    />
                  </div>
                )}
                <div>
                  <label htmlFor="org-description" className="block text-xs uppercase tracking-wider text-[var(--muted)] mb-1">説明</label>
                  <textarea
                    id="org-description"
                    rows={3}
                    placeholder="部門の役割など"
                    value={form.description}
                    onChange={(e) => setForm({ ...form, description: e.target.value })}
                    className="w-full px-3 py-2 rounded-xl border border-[var(--border)] bg-[var(--background)] text-sm focus:outline-none focus:border-[var(--primary)] resize-none"
                  />
                </div>
              </div>
              <div className="flex justify-end gap-2 mt-5">
                <button
                  type="button"
                  onClick={closeModal}
                  className="px-4 py-2 rounded-xl text-sm border border-[var(--border)] hover:bg-[var(--surface)] transition-colors"
                >
                  キャンセル
                </button>
                <button
                  type="submit"
                  disabled={submitting}
                  className="px-4 py-2 rounded-xl text-sm bg-[var(--primary)] text-white hover:bg-[var(--primary-hover)] transition-colors disabled:opacity-60"
                >
                  {submitting ? "送信中..." : submitLabel}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {loading && (
        <div className="bg-[var(--card)] border border-[var(--border)] rounded-2xl p-12 text-center">
          <p className="text-[var(--muted)] text-sm">読み込み中...</p>
        </div>
      )}

      {!loading && (
        <div className="bg-[var(--card)] border border-[var(--border)] rounded-2xl overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-[var(--surface)] border-b border-[var(--border)]">
              <tr>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">組織名</th>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">階層</th>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">説明</th>
                <th className="text-right px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">操作</th>
              </tr>
            </thead>
            <tbody>
              {orgs.length === 0 ? (
                <tr>
                  <td colSpan={4} className="px-5 py-12 text-center text-[var(--muted)]">
                    <p className="text-lg mb-1">△</p>
                    <p className="text-sm">組織データがありません</p>
                  </td>
                </tr>
              ) : (
                orgs.map((org) => (
                  <tr key={org.orgId} className="border-b border-[var(--border)] hover:bg-[var(--card-hover)] transition-colors">
                    <td className="px-5 py-3.5 font-medium" style={{ paddingLeft: `${org.level * 24 + 20}px` }}>
                      {org.level > 0 && <span className="text-[var(--accent)] mr-2">└</span>}
                      {org.name}
                    </td>
                    <td className="px-5 py-3.5">
                      <span className="inline-block px-2.5 py-1 rounded-full text-xs bg-[var(--accent-light)] text-[var(--muted)]">
                        Lv.{org.level}
                      </span>
                    </td>
                    <td className="px-5 py-3.5 text-[var(--muted)] text-xs">{org.description || "—"}</td>
                    <td className="px-5 py-3.5 text-right">
                      <button onClick={() => openEdit(org)}
                        className="text-xs px-2.5 py-1.5 rounded-lg border border-[var(--border)] hover:bg-[var(--surface)] transition-colors">
                        編集
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
