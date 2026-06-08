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
  const [records, setRecords] = useState<AttendanceWithName[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function load() {
      try {
        // Get all employees, then fetch attendance for each
        const empData = await apiFetch<{ employees: Employee[] }>("/api/employees");
        const employees = empData.employees ?? [];
        const today = new Date().toISOString().split("T")[0];
        const startDate = today;
        const endDate = today;

        const allRecords: AttendanceWithName[] = [];
        for (const emp of employees) {
          try {
            const attData = await apiFetch<{ records: AttendanceRecord[] }>(
              `/api/attendance?employeeId=${emp.employeeId}&startDate=${startDate}&endDate=${endDate}`
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
    }
    load();
  }, []);

  return (
    <div>
      <div className="flex items-center justify-between mb-2">
        <h2 className="text-xl font-semibold tracking-wide">勤怠管理</h2>
        <div className="flex gap-2">
          <button className="bg-[var(--primary)] text-white px-4 py-2 rounded-xl text-sm hover:bg-[var(--primary-hover)] transition-colors">
            出勤
          </button>
          <button className="bg-[var(--card)] text-[var(--foreground)] border border-[var(--border)] px-4 py-2 rounded-xl text-sm hover:bg-[var(--surface)] transition-colors">
            退勤
          </button>
        </div>
      </div>
      <p className="text-sm text-[var(--muted)] mb-6">本日の出退勤状況</p>

      {loading && (
        <div className="bg-[var(--card)] border border-[var(--border)] rounded-2xl p-12 text-center">
          <p className="text-[var(--muted)] text-sm">読み込み中...</p>
        </div>
      )}
      {error && (
        <div className="bg-[var(--danger-light)] border border-[var(--danger)] text-[var(--danger)] rounded-2xl p-4 mb-4 text-sm">
          {error}
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
