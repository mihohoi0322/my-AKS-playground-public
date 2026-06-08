"use client";

import { useEffect, useRef, useState } from "react";
import { apiFetch } from "@/lib/api";

interface Employee {
  employeeId: string;
  name: string;
  email: string;
  departmentId: string;
  position: string;
  hireDate: string;
  status: string;
}

type ModalState =
  | { mode: "create" }
  | { mode: "edit"; item: Employee }
  | null;

interface EmployeeForm {
  name: string;
  email: string;
  position: string;
  hireDate: string;
  departmentId: string;
}

const emptyForm: EmployeeForm = {
  name: "",
  email: "",
  position: "",
  hireDate: "",
  departmentId: "",
};

export default function EmployeesPage() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [modal, setModal] = useState<ModalState>(null);
  const [form, setForm] = useState<EmployeeForm>(emptyForm);
  const [submitting, setSubmitting] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);

  const fetchEmployees = async () => {
    const data = await apiFetch<{ employees: Employee[] }>("/api/employees");
    return data.employees ?? [];
  };

  useEffect(() => {
    fetchEmployees()
      .then(setEmployees)
      .catch((err: unknown) => setError(err instanceof Error ? err.message : "Unknown error"))
      .finally(() => setLoading(false));
  }, []);

  const closeModal = () => {
    setModal(null);
    // Restore focus to the trigger button after the modal unmounts.
    requestAnimationFrame(() => triggerRef.current?.focus());
  };

  // Close modal on Escape.
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

  const openEdit = (emp: Employee) => {
    setForm({
      name: emp.name,
      email: emp.email,
      position: emp.position,
      hireDate: emp.hireDate,
      departmentId: emp.departmentId,
    });
    setModal({ mode: "edit", item: emp });
  };

  const reload = async () => {
    setLoading(true);
    try {
      const data = await fetchEmployees();
      setEmployees(data);
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
        await apiFetch("/api/employees", {
          method: "POST",
          body: JSON.stringify({
            name: form.name,
            email: form.email,
            hireDate: form.hireDate,
            position: form.position,
            departmentId: form.departmentId,
          }),
        });
      } else {
        await apiFetch(`/api/employees/${modal.item.employeeId}`, {
          method: "PATCH",
          body: JSON.stringify({
            name: form.name,
            email: form.email,
            position: form.position,
          }),
        });
      }
      closeModal();
      await reload();
    } catch (err: unknown) {
      const fallback = modal.mode === "create" ? "登録に失敗しました" : "保存に失敗しました";
      setError(err instanceof Error ? err.message : fallback);
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async (emp: Employee) => {
    if (!confirm(`${emp.name} を削除しますか？`)) return;
    setError(null);
    try {
      await apiFetch(`/api/employees/${emp.employeeId}`, { method: "DELETE" });
      await reload();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "削除に失敗しました");
    }
  };

  const isCreate = modal?.mode === "create";
  const modalTitle = isCreate ? "従業員を追加" : "従業員を編集";
  const submitLabel = isCreate ? "追加" : "保存";

  return (
    <div>
      <div className="flex items-center justify-between mb-2">
        <h2 className="text-xl font-semibold tracking-wide">従業員一覧</h2>
        <button
          ref={triggerRef}
          onClick={openCreate}
          className="bg-[var(--primary)] text-white px-4 py-2 rounded-xl text-sm hover:bg-[var(--primary-hover)] transition-colors"
        >
          + 追加
        </button>
      </div>
      <p className="text-sm text-[var(--muted)] mb-6">従業員の管理・検索</p>

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
            aria-labelledby="emp-modal-title"
            className="bg-[var(--card)] border border-[var(--border)] rounded-2xl p-6 w-full max-w-md shadow-lg"
            onClick={(e) => e.stopPropagation()}
          >
            <h3 id="emp-modal-title" className="text-lg font-semibold mb-4">{modalTitle}</h3>
            <form onSubmit={handleSubmit}>
              <div className="space-y-3">
                <div>
                  <label htmlFor="emp-name" className="block text-xs uppercase tracking-wider text-[var(--muted)] mb-1">名前</label>
                  <input
                    id="emp-name"
                    type="text"
                    required
                    autoFocus
                    placeholder="山田 太郎"
                    value={form.name}
                    onChange={(e) => setForm({ ...form, name: e.target.value })}
                    className="w-full px-3 py-2 rounded-xl border border-[var(--border)] bg-[var(--background)] text-sm focus:outline-none focus:border-[var(--primary)]"
                  />
                </div>
                <div>
                  <label htmlFor="emp-email" className="block text-xs uppercase tracking-wider text-[var(--muted)] mb-1">メール</label>
                  <input
                    id="emp-email"
                    type="email"
                    required
                    placeholder="yamada@example.com"
                    value={form.email}
                    onChange={(e) => setForm({ ...form, email: e.target.value })}
                    className="w-full px-3 py-2 rounded-xl border border-[var(--border)] bg-[var(--background)] text-sm focus:outline-none focus:border-[var(--primary)]"
                  />
                </div>
                {isCreate && (
                  <div>
                    <label htmlFor="emp-hireDate" className="block text-xs uppercase tracking-wider text-[var(--muted)] mb-1">入社日</label>
                    <input
                      id="emp-hireDate"
                      type="date"
                      required
                      value={form.hireDate}
                      onChange={(e) => setForm({ ...form, hireDate: e.target.value })}
                      className="w-full px-3 py-2 rounded-xl border border-[var(--border)] bg-[var(--background)] text-sm focus:outline-none focus:border-[var(--primary)]"
                    />
                  </div>
                )}
                <div>
                  <label htmlFor="emp-position" className="block text-xs uppercase tracking-wider text-[var(--muted)] mb-1">役職</label>
                  <input
                    id="emp-position"
                    type="text"
                    placeholder="例: シニアエンジニア"
                    value={form.position}
                    onChange={(e) => setForm({ ...form, position: e.target.value })}
                    className="w-full px-3 py-2 rounded-xl border border-[var(--border)] bg-[var(--background)] text-sm focus:outline-none focus:border-[var(--primary)]"
                  />
                </div>
                {isCreate && (
                  <div>
                    <label htmlFor="emp-departmentId" className="block text-xs uppercase tracking-wider text-[var(--muted)] mb-1">部署 ID</label>
                    <input
                      id="emp-departmentId"
                      type="text"
                      placeholder="（任意）後で設定可"
                      value={form.departmentId}
                      onChange={(e) => setForm({ ...form, departmentId: e.target.value })}
                      className="w-full px-3 py-2 rounded-xl border border-[var(--border)] bg-[var(--background)] text-sm focus:outline-none focus:border-[var(--primary)]"
                    />
                  </div>
                )}
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
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">名前</th>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">メール</th>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">役職</th>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">入社日</th>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">状態</th>
                <th className="text-right px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">操作</th>
              </tr>
            </thead>
            <tbody>
              {employees.length === 0 ? (
                <tr>
                  <td colSpan={6} className="px-5 py-12 text-center text-[var(--muted)]">
                    <p className="text-lg mb-1">○</p>
                    <p className="text-sm">従業員データがありません</p>
                  </td>
                </tr>
              ) : (
                employees.map((emp) => (
                  <tr key={emp.employeeId} className="border-b border-[var(--border)] hover:bg-[var(--card-hover)] transition-colors">
                    <td className="px-5 py-3.5 font-medium">{emp.name}</td>
                    <td className="px-5 py-3.5 text-[var(--muted)]">{emp.email}</td>
                    <td className="px-5 py-3.5">{emp.position}</td>
                    <td className="px-5 py-3.5 text-[var(--muted)]">{emp.hireDate}</td>
                    <td className="px-5 py-3.5">
                      <span className={`inline-block px-2.5 py-1 rounded-full text-xs font-medium ${
                        emp.status === "active"
                          ? "bg-[var(--success-light)] text-[var(--success)]"
                          : "bg-[var(--surface)] text-[var(--muted)]"
                      }`}>
                        {emp.status === "active" ? "在籍" : emp.status}
                      </span>
                    </td>
                    <td className="px-5 py-3.5 text-right">
                      <button onClick={() => openEdit(emp)}
                        className="text-xs px-2.5 py-1.5 rounded-lg border border-[var(--border)] hover:bg-[var(--surface)] transition-colors mr-1">
                        編集
                      </button>
                      <button onClick={() => handleDelete(emp)}
                        className="text-xs px-2.5 py-1.5 rounded-lg border border-[var(--danger)] text-[var(--danger)] hover:bg-[var(--danger-light)] transition-colors">
                        削除
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
