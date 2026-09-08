# Homebrew's dotnet@8 is keg-only, so tooling that shells out to the SDK — most
# notably dotnet-ef — cannot find it without this. Kept here rather than in a
# shell profile so the repository stays self-contained.
DOTNET_ROOT ?= $(shell brew --prefix dotnet@8 2>/dev/null)/libexec
export DOTNET_ROOT
export PATH := $(PATH):$(HOME)/.dotnet/tools

BACKEND        := backend
SOLUTION       := $(BACKEND)/WeatherService.sln
API            := $(BACKEND)/src/WeatherService.Api
INFRASTRUCTURE := $(BACKEND)/src/WeatherService.Infrastructure
UNIT_TESTS     := $(BACKEND)/tests/WeatherService.UnitTests
INTEGRATION    := $(BACKEND)/tests/WeatherService.IntegrationTests
FRONTEND       := frontend

ifneq (,$(wildcard .env))
    include .env
    export
endif

.DEFAULT_GOAL := help

# ------------------------------------------------------------------ everything

.PHONY: help
help: ## Show this help
	@grep -hE '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) \
		| awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-22s\033[0m %s\n", $$1, $$2}'

.PHONY: env
env: ## Create .env from the template
	@test -f .env && echo ".env already exists, leaving it alone" \
		|| (cp env.example .env && echo "created .env")

.PHONY: up
up: env ## Build and run the whole stack (Postgres + API + web)
	docker compose up --build

.PHONY: down
down: ## Stop the stack
	docker compose down

.PHONY: clean
clean: ## Stop the stack and delete its data volume
	docker compose down --volumes

.PHONY: logs
logs: ## Follow the stack logs
	docker compose logs --follow

# --------------------------------------------------------------------- backend

.PHONY: build
build: ## Build the solution
	dotnet build $(SOLUTION)

.PHONY: api
api: ## Run the API locally against SQLite, no Docker required
	ASPNETCORE_ENVIRONMENT=Development dotnet watch --project $(API) run

.PHONY: test
test: ## Run every test
	dotnet test $(SOLUTION)

.PHONY: test-unit
test-unit: ## Run the unit tests only
	dotnet test $(UNIT_TESTS)

.PHONY: test-integration
test-integration: ## Run the integration tests only
	dotnet test $(INTEGRATION)

.PHONY: test-watch
test-watch: ## Re-run the unit tests on every change
	dotnet watch --project $(UNIT_TESTS) test

# ------------------------------------------------------------------ migrations
# Migrations live in Infrastructure (next to the DbContext) while the API is the
# startup project, because that is where configuration and the Design package
# are. Postgres and SQLite emit incompatible DDL, so each keeps its own history.

.PHONY: migration-add
migration-add: ## Add a migration for both providers: make migration-add name=AddThing
	@test -n "$(name)" || (echo "usage: make migration-add name=<MigrationName>" && exit 1)
	dotnet-ef migrations add $(name) --context PostgresWeatherDbContext \
		--project $(INFRASTRUCTURE) --startup-project $(API) \
		--output-dir Persistence/Migrations/Postgres
	dotnet-ef migrations add $(name) --context SqliteWeatherDbContext \
		--project $(INFRASTRUCTURE) --startup-project $(API) \
		--output-dir Persistence/Migrations/Sqlite

.PHONY: migration-list
migration-list: ## List applied and pending migrations for Postgres
	dotnet-ef migrations list --context PostgresWeatherDbContext \
		--project $(INFRASTRUCTURE) --startup-project $(API)

.PHONY: migration-up
migration-up: ## Apply pending Postgres migrations
	dotnet-ef database update --context PostgresWeatherDbContext \
		--project $(INFRASTRUCTURE) --startup-project $(API)

.PHONY: migration-tools
migration-tools: ## Install the dotnet-ef CLI
	dotnet tool install --global dotnet-ef --version '8.0.11' \
		|| dotnet tool update --global dotnet-ef --version '8.0.11'

# -------------------------------------------------------------------- frontend

.PHONY: web-install
web-install: ## Install front-end dependencies
	npm install --prefix $(FRONTEND)

.PHONY: web
web: ## Run the front end in dev mode on :5173
	npm run dev --prefix $(FRONTEND)

.PHONY: web-build
web-build: ## Type-check and build the front end
	npm run build --prefix $(FRONTEND)

.PHONY: web-lint
web-lint: ## Lint the front end
	npm run lint --prefix $(FRONTEND)
