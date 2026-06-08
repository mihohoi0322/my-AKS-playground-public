SHELL := /bin/bash

# Colors for output
GREEN := \033[0;32m
YELLOW := \033[0;33m
RED := \033[0;31m
NC := \033[0m # No Color

.PHONY: help install dev build start test test-cov lint typecheck format format-check qa clean docker-build load-smoke load-baseline load-stress load-spike

help: ## Show help
	@echo 'Usage: make [target]'
	@echo ''
	@echo 'Targets:'
	@awk 'BEGIN {FS = ":.*?## "} /^[a-zA-Z_-]+:.*?## / {printf "  %-18s %s\n", $$1, $$2}' $(MAKEFILE_LIST)

install: ## Install all dependencies (workspaces)
	@echo -e "$(YELLOW)Installing dependencies...$(NC)"
	@npm ci
	@echo -e "$(GREEN)✓ Dependencies installed$(NC)"

dev: ## Run api-gateway locally (watch mode)
	@echo -e "$(YELLOW)Starting api-gateway on http://localhost:8000...$(NC)"
	@npm run dev --workspace=@hrsystem/api-gateway

build: ## Build all packages
	@echo -e "$(YELLOW)Building all packages...$(NC)"
	@npm run build --workspaces
	@echo -e "$(GREEN)✓ Build complete$(NC)"

start: ## Run built api-gateway
	@npm run start --workspace=@hrsystem/api-gateway

test: ## Run unit tests (api-gateway)
	@echo -e "$(YELLOW)Running unit tests...$(NC)"
	@npm run test:unit --workspace=@hrsystem/api-gateway
	@echo -e "$(GREEN)✓ Unit tests passed$(NC)"

test-cov: ## Run unit tests with coverage report
	@echo -e "$(YELLOW)Running unit tests with coverage...$(NC)"
	@npm run test:cov --workspace=@hrsystem/api-gateway
	@echo -e "$(GREEN)✓ Coverage report generated$(NC)"

lint: ## Run linting (all workspaces)
	@echo -e "$(YELLOW)Running linter...$(NC)"
	@npm run lint --workspaces --if-present
	@echo -e "$(GREEN)✓ Linting complete$(NC)"

typecheck: ## Run type checking (all workspaces)
	@echo -e "$(YELLOW)Running type checker...$(NC)"
	@npm run typecheck --workspaces --if-present
	@echo -e "$(GREEN)✓ Type checking complete$(NC)"

format: ## Format code
	@echo -e "$(YELLOW)Formatting code...$(NC)"
	@npx prettier --write .
	@echo -e "$(GREEN)✓ Code formatted$(NC)"

format-check: ## Check code formatting (no changes)
	@echo -e "$(YELLOW)Checking code format...$(NC)"
	@npx prettier --check .
	@echo -e "$(GREEN)✓ Formatting check passed$(NC)"

qa: ## Lint + Test + Typecheck (api-gateway + web-ui)
	@echo -e "$(YELLOW)Running QA checks...$(NC)"
	@npm run qa --workspace=@hrsystem/api-gateway
	@npm run qa --workspace=@hrsystem/web-ui
	@echo -e "$(GREEN)✓ All QA checks passed$(NC)"

clean: ## Clean build artifacts and caches
	@echo -e "$(YELLOW)Cleaning...$(NC)"
	@npm run clean --workspaces --if-present
	@rm -rf node_modules/.cache
	@echo -e "$(GREEN)✓ Cleaned$(NC)"

docker-build: ## Build Docker image locally
	@echo -e "$(YELLOW)Building Docker image...$(NC)"
	@docker build -f packages/api-gateway/Dockerfile -t aks-hrsystem-api-gateway:local .
	@echo -e "$(GREEN)✓ Docker image built$(NC)"

load-smoke: ## Run load test (smoke profile)
	cd packages/api-gateway/tests/load && ./run-load-tests.sh smoke

load-baseline: ## Run load test (baseline profile)
	cd packages/api-gateway/tests/load && ./run-load-tests.sh baseline

load-stress: ## Run load test (stress profile)
	cd packages/api-gateway/tests/load && ./run-load-tests.sh stress

load-spike: ## Run load test (spike profile)
	cd packages/api-gateway/tests/load && ./run-load-tests.sh spike
