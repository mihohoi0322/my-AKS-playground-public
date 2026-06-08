"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "@/lib/api";

interface Employee {
  employeeId: string;
  name: string;
}

interface AttendanceRecord {
  attendanceId: string;
  employeeId: string;
  date: string;
  clockIn: string;
  clockOut: string;
  workHours: number;
  type: string;
}

interface AttendanceWithName extends AttendanceRecord {
  employeeName: string;
}

export default function AttendancePage() {
  const [employees, setEmployees] = useState<Employee[]>([]);
  const [selectedEmployeeId, setSelectedEmployeeId] = useState<string>("");
  const [records, setRecords] = useState<AttendanceWithName[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const loadAll = async () => {
    setLoading(true);
    setError(null);
    try {
      const empData = await apiFetch<{ employees: Employee[] }>("/api/employees");
      const emps = empData.employees ?? [];
      setEmployees(emps);
      if (emps.length > 0) {
        setSelectedEmployeeId((prev) => prev || emps[0].employeeId);
      }
      const today = new Date().toISOString().split("T")[0];
      const allRecords: AttendanceWithName[] = [];
      for (const emp of emps) {
        try {
          const attData = await apiFetch<{ records: AttendanceRecord[] }>(
            `/api/attendance?employeeId=${emp.employeeId}&startDate=${today}&endDate=${today}`,
          );
          for (const rec of attData.records ?? []) {
            allRecords.push({ ...rec, employeeName: emp.name });
          }
        } catch {
          // Skip employees without attendance data
        }
      }
      setRecords(allRecords);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Unknown error");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void loadAll();
  }, []);

  const handleClockIn = async () => {
    if (!selectedEmployeeId) {
      setError("従業員を選択してください");
      return;
    }
    setError(null);
    setInfo(null);
    setSubmitting(true);
    try {
      await apiFetch("/api/attendance/clock-in", {
        method: "POST",
        body: JSON.stringify({ employeeId: selectedEmployeeId, type: "office" }),
      });
      const emp = employees.find((e) => e.employeeId === selectedEmployeeId);
      setInfo(`${emp?.name ?? selectedEmployeeId} の出勤を記録しました`);
      await loadAll();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "出勤の記録に失敗しました");
    } finally {
      setSubmitting(false);
    }
  };

  const handleClockOut = async () => {
    if (!selectedEmployeeId) {
      setError("従業員を選択してください");
      return;
    }
    setError(null);
    setInfo(null);
    setSubmitting(true);
    try {
      await apiFetch("/api/attendance/clock-out", {
        method: "POST",
        body: JSON.stringify({ employeeId: selectedEmployeeId }),
      });
      const emp = employees.find((e) => e.employeeId === selectedEmployeeId);
      setInfo(`${emp?.name ?? selectedEmployeeId} の退勤を記録しました`);
      await loadAll();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "退勤の記録に失敗しました");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div>
      <div className="flex items-center justify-between mb-2 gap-4 flex-wrap">
        <h2 className="text-xl font-semibold tracking-wide">勤怠管理</h2>
        <div className="flex gap-2 items-center flex-wrap">
          <select
            aria-label="打刻対象の従業員"
            value={selectedEmployeeId}
            onChange={(e) => setSelectedEmployeeId(e.target.value)}
            disabled={employees.length === 0 || submitting}
            className="px-3 py-2 rounded-xl border border-[var(--border)] bg-[var(--background)] text-sm focus:outline-none focus:border-[var(--primary)]"
          >
            {employees.length === 0 ? (
              <option value="">従業員がいません</option>
            ) : (
              employees.map((emp) => (
                <option key={emp.employeeId} value={emp.employeeId}>
                  {emp.name}
                </option>
              ))
            )}
          </select>
          <button
            onClick={handleClockIn}
            disabled={submitting || !selectedEmployeeId}
            className="bg-[var(--primary)] text-white px-4 py-2 rounded-xl text-sm hover:bg-[var(--primary-hover)] transition-colors disabled:opacity-60"
          >
            出勤
          </button>
          <button
            onClick={handleClockOut}
            disabled={submitting || !selectedEmployeeId}
            className="bg-[var(--card)] text-[var(--foreground)] border border-[var(--border)] px-4 py-2 rounded-xl text-sm hover:bg-[var(--surface)] transition-colors disabled:opacity-60"
          >
            退勤
          </button>
        </div>
      </div>
      <p className="text-sm text-[var(--muted)] mb-6">本日の出退勤状況</p>

      {info && (
        <div className="bg-[var(--success-light)] border border-[var(--success)] text-[var(--success)] rounded-2xl p-4 mb-4 text-sm">
          {info}
          <button onClick={() => setInfo(null)} className="ml-2 underline">閉じる</button>
        </div>
      )}

      {loading && (
        <div className="bg-[var(--card)] border border-[var(--border)] rounded-2xl p-12 text-center">
          <p className="text-[var(--muted)] text-sm">読み込み中...</p>
        </div>
      )}
      {error && (
        <div className="bg-[var(--danger-light)] border border-[var(--danger)] text-[var(--danger)] rounded-2xl p-4 mb-4 text-sm">
          {error}
          <button onClick={() => setError(null)} className="ml-2 underline">閉じる</button>
        </div>
      )}

      {!loading && !error && (
        <div className="bg-[var(--card)] border border-[var(--border)] rounded-2xl overflow-hidden">
          <table className="w-full text-sm">
            <thead className="bg-[var(--surface)] border-b border-[var(--border)]">
              <tr>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">従業員</th>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">出勤</th>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">退勤</th>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">種別</th>
                <th className="text-left px-5 py-3.5 font-medium text-xs uppercase tracking-wider text-[var(--muted)]">勤務時間</th>
              </tr>
            </thead>
            <tbody>
              {records.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-5 py-12 text-center text-[var(--muted)]">
                    <p className="text-lg mb-1">◷</p>
                    <p className="text-sm">本日の勤怠データがありません</p>
                  </td>
                </tr>
              ) : (
                records.map((rec) => (
                  <tr key={rec.attendanceId} className="border-b border-[var(--border)] hover:bg-[var(--card-hover)] transition-colors">
                    <td className="px-5 py-3.5 font-medium">{rec.employeeName}</td>
                    <td className="px-5 py-3.5">{new Date(rec.clockIn).toLocaleTimeString("ja-JP", { hour: "2-digit", minute: "2-digit" })}</td>
                    <td className="px-5 py-3.5">
                      {rec.clockOut
                        ? new Date(rec.clockOut).toLocaleTimeString("ja-JP", { hour: "2-digit", minute: "2-digit" })
                        : <span className="inline-block px-2.5 py-1 rounded-full text-xs font-medium bg-[var(--success-light)] text-[var(--success)]">勤務中</span>}
                    </td>
                    <td className="px-5 py-3.5">
                      <span className={`inline-block px-2.5 py-1 rounded-full text-xs ${
                        rec.type === "remote"
                          ? "bg-blue-50 text-blue-600"
                          : "bg-[var(--surface)] text-[var(--muted)]"
                      }`}>
                        {rec.type === "remote" ? "リモート" : "出社"}
                      </span>
                    </td>
                    <td className="px-5 py-3.5 text-[var(--muted)]">
                      {rec.workHours > 0 ? `${rec.workHours.toFixed(1)}h` : "—"}
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
