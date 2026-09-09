# Homebrew's dotnet@8 is keg-only, so tooling that shells out to the SDK — most
# notably dotnet-ef — cannot find it without this. Kept here rather than in a
# shell profile so the repository stays self-contained.
DOTNET_ROOT ?= $(shell brew --prefix dotnet@8 2>/dev/null)/libexec
export DOTNET_ROOT
export PATH := $(PATH):$(HOME)/.dotnet/tools

# Global dotnet tools land here, and they are named by full path on purpose:
# make skips the shell for a recipe with no shell metacharacters, and that path
# resolves commands against make's own PATH rather than the one exported above.
# It is why `cd x && dotnet-sonarscanner` finds its tool and a bare `dotnet-ef`
# does not.
DOTNET_TOOLS := $(HOME)/.dotnet/tools
DOTNET_EF    := $(DOTNET_TOOLS)/dotnet-ef

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
	$(DOTNET_EF) migrations add $(name) --context PostgresWeatherDbContext \
		--project $(INFRASTRUCTURE) --startup-project $(API) \
		--output-dir Persistence/Migrations/Postgres
	$(DOTNET_EF) migrations add $(name) --context SqliteWeatherDbContext \
		--project $(INFRASTRUCTURE) --startup-project $(API) \
		--output-dir Persistence/Migrations/Sqlite

.PHONY: migration-list
migration-list: ## List applied and pending migrations for Postgres
	$(DOTNET_EF) migrations list --context PostgresWeatherDbContext \
		--project $(INFRASTRUCTURE) --startup-project $(API)

.PHONY: migration-up
migration-up: ## Apply pending Postgres migrations
	$(DOTNET_EF) database update --context PostgresWeatherDbContext \
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

.PHONY: web-test
web-test: ## Run the front-end tests
	npm test --prefix $(FRONTEND)

.PHONY: web-test-watch
web-test-watch: ## Re-run the front-end tests on every change
	npm run test:watch --prefix $(FRONTEND)

# ------------------------------------------------------- quality (SonarQube)
# The subjective half of a review is a human reading the diff. This is the
# objective half: a real SonarQube instance, run locally, so the quality numbers
# quoted anywhere in this repository can be reproduced rather than trusted.
#
# It is a separate compose project from `make up` on purpose — see
# quality/docker-compose.yml.

QUALITY      := quality
SONAR_URL    ?= http://localhost:9001
SONAR_NET    := weather-quality_default
SONAR_TOKEN   = $(shell cat $(QUALITY)/.sonar-token 2>/dev/null)
SONAR_COMPOSE := docker compose -f $(QUALITY)/docker-compose.yml

.PHONY: sonar-up
sonar-up: ## Start the local SonarQube and provision an analysis token
	SONAR_URL=$(SONAR_URL) ./$(QUALITY)/sonar-up.sh

.PHONY: sonar-tools
sonar-tools: ## Install the SonarScanner for .NET
	@# --framework net8.0 is load-bearing: the default install resolves an
	@# osx-x64 apphost that demands a .NET 10 runtime, which fails on Apple
	@# Silicon boxes carrying only the .NET 8 SDK this project targets.
	dotnet tool install --global dotnet-sonarscanner --framework net8.0 \
		|| dotnet tool update --global dotnet-sonarscanner --framework net8.0

.PHONY: sonar-scan
sonar-scan: sonar-scan-api sonar-scan-web sonar-report ## Analyse both projects and print the report

.PHONY: sonar-scan-api
sonar-scan-api: sonar-tools ## Analyse the backend
	@test -n "$(SONAR_TOKEN)" || (echo "no analysis token — run: make sonar-up" && exit 1)
	@# The C# analyser only runs as an MSBuild pass, so begin/build/end is not
	@# optional here: without the build in the middle it indexes the files and
	@# applies no C# rule at all. The build turns TreatWarningsAsErrors off
	@# because the injected Sonar analysers raise warnings of their own, and a
	@# quality scan that cannot compile reports nothing.
	cd $(BACKEND) && dotnet-sonarscanner begin \
		/k:"weather-backend" /n:"Weather Backend" \
		/d:sonar.host.url="$(SONAR_URL)" \
		/d:sonar.token="$(SONAR_TOKEN)" \
		/d:sonar.scanner.scanAll=false \
		/d:sonar.exclusions="**/Migrations/**"
	cd $(BACKEND) && dotnet build WeatherService.sln --no-incremental -p:TreatWarningsAsErrors=false
	cd $(BACKEND) && dotnet-sonarscanner end /d:sonar.token="$(SONAR_TOKEN)"

.PHONY: sonar-scan-web
sonar-scan-web: ## Analyse the front end
	@test -n "$(SONAR_TOKEN)" || (echo "no analysis token — run: make sonar-up" && exit 1)
	@# Run on SonarQube's own compose network and address it by service name:
	@# a container cannot reach the host's published port on macOS.
	docker run --rm \
		--network $(SONAR_NET) \
		-e SONAR_HOST_URL="http://sonarqube:9000" \
		-e SONAR_TOKEN="$(SONAR_TOKEN)" \
		-v "$(CURDIR)/$(FRONTEND):/usr/src" \
		sonarsource/sonar-scanner-cli \
		-Dsonar.projectKey=weather-frontend \
		-Dsonar.projectName="Weather Frontend" \
		-Dsonar.sources=src \
		-Dsonar.inclusions="src/**/*.ts,src/**/*.tsx,src/**/*.css,index.html" \
		-Dsonar.exclusions="node_modules/**,dist/**" \
		-Dsonar.tests=src \
		-Dsonar.test.inclusions="src/**/*.test.ts,src/**/*.test.tsx,src/test/**" \
		-Dsonar.sourceEncoding=UTF-8

.PHONY: sonar-report
sonar-report: ## Print the quality report for both projects
	@SONAR_URL=$(SONAR_URL) ./$(QUALITY)/sonar-report.py

.PHONY: sonar-open
sonar-open: ## Open the SonarQube dashboard
	@open $(SONAR_URL) 2>/dev/null || echo "$(SONAR_URL)"

.PHONY: sonar-down
sonar-down: ## Stop SonarQube, keeping its analysis history
	$(SONAR_COMPOSE) down

.PHONY: sonar-clean
sonar-clean: ## Stop SonarQube and delete its volumes and token
	$(SONAR_COMPOSE) down --volumes
	@rm -f $(QUALITY)/.sonar-token
