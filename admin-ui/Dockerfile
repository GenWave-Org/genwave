FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

FROM node:22-alpine AS builder
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .
# next.config.ts rewrites() (/api/* -> backend) are serialized at BUILD time, so BACKEND_URL must be
# present here, not just at runtime. In this compose the backend is always the `api` service.
ARG BACKEND_URL=http://api:8080
ENV BACKEND_URL=$BACKEND_URL
RUN npm run build

FROM node:22-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production
# Next.js standalone server.js binds to the HOSTNAME env var; Docker sets HOSTNAME to the container
# ID, so without this it listens only on the container's eth0 IP and the healthcheck's localhost
# probe is refused. Pin to 0.0.0.0 (all interfaces, incl. loopback).
ENV HOSTNAME=0.0.0.0
ENV PORT=3000
COPY --from=builder /app/.next/standalone ./
COPY --from=builder /app/.next/static ./.next/static
COPY --from=builder /app/public ./public
EXPOSE 3000
CMD ["node", "server.js"]
