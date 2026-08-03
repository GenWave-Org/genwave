import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  output: "standalone",
  // Proxy API requests to the C# backend at runtime
  // The BACKEND_URL env var is set in compose; in dev it falls back to localhost
  async rewrites() {
    const backendUrl = process.env["BACKEND_URL"] ?? "http://localhost:5000";
    return [
      {
        source: "/api/:path*",
        destination: `${backendUrl}/api/:path*`,
      },
      // The canonical vendored-font route (PLAN T173, SPEC F102): globals.css's @font-face rules
      // reference /fonts/{file} same-origin; this rewrite is what makes that same-origin path
      // resolve to GenWave.Host's GET /fonts/{file} instead of 404ing inside admin-ui itself.
      {
        source: "/fonts/:path*",
        destination: `${backendUrl}/fonts/:path*`,
      },
    ];
  },
};

export default nextConfig;
