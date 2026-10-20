import { readFileSync, readdirSync } from "node:fs";
import initSqlJs from "sql.js";

const SQL = await initSqlJs();
const migrations = new URL("../migrations/", import.meta.url);
const schema = readdirSync(migrations).filter(name => name.endsWith(".sql")).sort()
  .map(name => readFileSync(new URL(name, migrations), "utf8")).join("\n");

// Execute the production SQL, including triggers, against SQLite.
export class Database {
  constructor() { this.db = new SQL.Database(); this.db.run("PRAGMA foreign_keys=ON;" + schema); }
  prepare(sql) {
    const run = values => {
      const query = this.db.prepare(sql);
      try {
        query.bind(values); const results = [];
        while (query.step()) results.push(query.getAsObject());
        return { results, success: true };
      } finally { query.free(); }
    };
    return { bind: (...values) => ({
      first: async () => run(values).results[0] ?? null,
      all: async () => run(values), run: async () => run(values),
    }) };
  }
  async batch(statements) {
    this.db.run("BEGIN");
    try { const result = []; for (const statement of statements) result.push(await statement.run()); this.db.run("COMMIT"); return result; }
    catch (e) { this.db.run("ROLLBACK"); throw e; }
  }
}
