FROM node:22-slim AS builder

WORKDIR /app

# Copy workspace root and api-gateway package files for install
COPY package.json package-lock.json ./
COPY packages/api-gateway/package.json packages/api-gateway/
COPY packages/shared/package.json packages/shared/

# Install all dependencies
RUN npm ci --workspace=@hrsystem/api-gateway

# Copy source code
COPY packages/api-gateway/tsconfig.json packages/api-gateway/
COPY packages/api-gateway/app packages/api-gateway/app

# Build TypeScript
RUN npm run build --workspace=@hrsystem/api-gateway

# --- Production stage ---
FROM node:22-slim

WORKDIR /app

# Copy workspace root and api-gateway package files
COPY package.json package-lock.json ./
COPY packages/api-gateway/package.json packages/api-gateway/
COPY packages/shared/package.json packages/shared/
RUN npm ci --workspace=@hrsystem/api-gateway --omit=dev && npm cache clean --force

# Copy built output from builder
COPY --from=builder /app/packages/api-gateway/dist packages/api-gateway/dist

# Copy proto files (needed at runtime for gRPC client)
COPY proto packages/proto

# Create non-root user and set permissions
RUN groupadd -g 10001 app && \
    useradd -r -u 10001 -g app app && \
    chown -R app:app /app

EXPOSE 3000

USER app:app

CMD ["node", "packages/api-gateway/dist/app/index.js"]
