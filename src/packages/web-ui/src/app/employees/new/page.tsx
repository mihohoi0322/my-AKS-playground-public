import { redirect } from "next/navigation";

export default function NewEmployeePage() {
  redirect("/employees?new=1");
}
